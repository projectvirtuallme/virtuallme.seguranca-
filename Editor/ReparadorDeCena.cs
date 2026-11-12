// VirtuALLme · Segurança do Projeto
// FERRAMENTA 1 — REPARADOR DE CENA
//
// • Sempre que a cena é guardada/aberta tira um "snapshot" (fotografia) leve de:
//     - que material está em cada slot de cada Renderer, e que mesh usa;
//     - que prefab está em cada instância (e onde está: pai, posição, rotação, escala);
//     - as cores, valores e texturas de cada material usado (+ shader).
//   Fica em  <Projeto>/VirtuALLme_Seguranca/Snapshots/<guid-da-cena>.json  (fora de Assets).
// • O botão "Reparar cena agora" usa esse snapshot para repor tudo o que se perdeu:
//     materiais vazios/"Missing", shaders partidos (rosa), texturas perdidas, meshes perdidas,
//     prefabs "Missing Prefab", materiais apagados (recria-os, até com o GUID original),
//     e — se quiseres — objetos que desapareceram desde o último "estado bom".
// • Mesmo sem snapshot repara materiais rosa (conversão Built-in → URP/HDRP) e prefabs em falta
//   procurando pelo nome.
// • Tudo com Undo (Ctrl+Z).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VirtuALLme.Seguranca
{
    #region Dados do snapshot

    [Serializable]
    public class RefAsset
    {
        public string guid;
        public long localId;
        public string nome;
        public string caminho;
        public string tipo;

        public bool Vazio { get { return string.IsNullOrEmpty(guid) && string.IsNullOrEmpty(nome); } }
        public string Chave { get { return string.IsNullOrEmpty(guid) ? "n:" + nome : guid + ":" + localId; } }
    }

    [Serializable]
    public class RegRenderer
    {
        public string goid;
        public string caminhoHierarquia;
        public string tipoComp;
        public List<RefAsset> materiais = new List<RefAsset>();
        public List<RefAsset> materiaisBom = new List<RefAsset>();
        public RefAsset mesh;
    }

    [Serializable]
    public class RegPrefab
    {
        public string goid;
        public string caminhoHierarquia;
        public string nome;
        public RefAsset prefab;
        public string paiGoid;
        public string paiCaminho;
        public int irmao;
        public Vector3 pos;
        public Quaternion rot = Quaternion.identity;
        public Vector3 escala = Vector3.one;
        public bool noEstadoBom;
    }

    [Serializable] public class PropCor { public string n; public Color v; }
    [Serializable] public class PropFloat { public string n; public float v; }
    [Serializable] public class PropVec { public string n; public Vector4 v; }

    [Serializable]
    public class PropTex
    {
        public string n;
        public RefAsset tex;
        public Vector2 escala = Vector2.one;
        public Vector2 offset;
    }

    [Serializable]
    public class RegMaterial
    {
        public RefAsset refMat;
        public string shader;
        public int renderQueue;
        public List<string> keywords = new List<string>();
        public List<PropCor> cores = new List<PropCor>();
        public List<PropFloat> floats = new List<PropFloat>();
        public List<PropVec> vecs = new List<PropVec>();
        public List<PropTex> texs = new List<PropTex>();
    }

    [Serializable]
    public class SnapshotCena
    {
        public string cenaGuid;
        public string cenaCaminho;
        public string atualizado;
        public string estadoBomEm;
        public RefAsset skybox;
        public List<RegRenderer> renderers = new List<RegRenderer>();
        public List<RegPrefab> prefabs = new List<RegPrefab>();
        public List<RegMaterial> materiais = new List<RegMaterial>();
        public List<RegMaterial> materiaisBom = new List<RegMaterial>();
    }

    public class Relatorio
    {
        public int prefabs, recriados, materiais, matsRecriados, shaders, props, meshes, skybox;
        public int scriptsEmFalta, slotsSemInfo;
        public readonly List<string> linhas = new List<string>();
        public readonly List<string> naoResolvidos = new List<string>();

        public int Total { get { return prefabs + recriados + materiais + matsRecriados + shaders + props + meshes + skybox; } }

        public string Texto()
        {
            var sb = new StringBuilder();
            if (prefabs > 0) sb.AppendLine("• Prefabs em falta repostos: " + prefabs);
            if (recriados > 0) sb.AppendLine("• Objetos apagados recriados: " + recriados);
            if (materiais > 0) sb.AppendLine("• Slots de material repostos: " + materiais);
            if (matsRecriados > 0) sb.AppendLine("• Materiais recriados/extraídos: " + matsRecriados);
            if (shaders > 0) sb.AppendLine("• Shaders partidos (rosa) corrigidos: " + shaders);
            if (props > 0) sb.AppendLine("• Cores/texturas/valores repostos: " + props);
            if (meshes > 0) sb.AppendLine("• Meshes repostas: " + meshes);
            if (skybox > 0) sb.AppendLine("• Skybox reposto");
            if (Total == 0) sb.AppendLine("Nada para reparar.");
            sb.Append(Avisos());
            return sb.ToString().TrimEnd();
        }

        public string Avisos()
        {
            var sb = new StringBuilder();
            if (scriptsEmFalta > 0) sb.AppendLine("⚠ Componentes com script em falta: " + scriptsEmFalta + " (botão \"Remover scripts em falta\" no painel)");
            if (slotsSemInfo > 0) sb.AppendLine("⚠ Slots de material vazios sem informação guardada: " + slotsSemInfo);
            if (naoResolvidos.Count > 0)
            {
                sb.AppendLine("⚠ Não consegui resolver " + naoResolvidos.Count + " item(ns) (ver Console):");
                foreach (var n in naoResolvidos.Take(6)) sb.AppendLine("   – " + n);
                if (naoResolvidos.Count > 6) sb.AppendLine("   …");
            }
            return sb.ToString();
        }
    }

    #endregion

    public static class ReparadorDeCena
    {
        public static string UltimoRelatorio = "";
        static readonly Dictionary<string, Object> cache = new Dictionary<string, Object>();

        static readonly Dictionary<string, string[]> Alias = new Dictionary<string, string[]>
        {
            { "_MainTex",   new[] { "_BaseMap", "_BaseColorMap" } },
            { "_BaseMap",   new[] { "_MainTex", "_BaseColorMap" } },
            { "_BaseColorMap", new[] { "_BaseMap", "_MainTex" } },
            { "_Color",     new[] { "_BaseColor" } },
            { "_BaseColor", new[] { "_Color" } },
            { "_Glossiness", new[] { "_Smoothness" } },
            { "_Smoothness", new[] { "_Glossiness" } },
            { "_BumpMap",   new[] { "_NormalMap" } },
            { "_NormalMap", new[] { "_BumpMap" } },
            { "_EmissionMap", new[] { "_EmissiveColorMap" } },
            { "_EmissionColor", new[] { "_EmissiveColor" } },
        };

        // ------------------------------------------------------------------ ficheiros
        static string Ficheiro(Scene cena)
        {
            string g = AssetDatabase.AssetPathToGUID(cena.path);
            if (string.IsNullOrEmpty(g)) g = Caminhos.NomeSeguro(cena.path);
            return Path.Combine(Caminhos.Snapshots, g + ".json");
        }

        public static bool TemSnapshot(Scene cena)
        {
            return !string.IsNullOrEmpty(cena.path) && File.Exists(Ficheiro(cena));
        }

        public static SnapshotCena Ler(Scene cena)
        {
            if (!cena.IsValid() || string.IsNullOrEmpty(cena.path)) return null;
            try
            {
                string f = Ficheiro(cena);
                if (!File.Exists(f)) return null;
                return JsonUtility.FromJson<SnapshotCena>(File.ReadAllText(f));
            }
            catch (Exception e)
            {
                Debug.LogWarning(Caminhos.Log + "Snapshot ilegível: " + e.Message);
                return null;
            }
        }

        static void Gravar(Scene cena, SnapshotCena snap)
        {
            Caminhos.Garantir(Caminhos.Snapshots);
            string f = Ficheiro(cena);
            if (File.Exists(f)) File.Copy(f, f + ".anterior", true);
            File.WriteAllText(f, JsonUtility.ToJson(snap, true));
        }

        // ------------------------------------------------------------------ utilitários
        public static IEnumerable<Scene> CenasAbertas()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.IsValid() && s.isLoaded) yield return s;
            }
        }

        static IEnumerable<GameObject> TodosGO(Scene cena)
        {
            foreach (var root in cena.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    yield return t.gameObject;
        }

        static IEnumerable<Renderer> TodosRenderers(Scene cena)
        {
            foreach (var root in cena.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    yield return r;
        }

        public static string CaminhoHierarquia(Transform t)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
            return sb.ToString();
        }

        static GameObject AcharPorCaminho(Scene cena, string caminho)
        {
            if (string.IsNullOrEmpty(caminho)) return null;
            string[] partes = caminho.Split('/');
            foreach (var root in cena.GetRootGameObjects())
            {
                if (root.name != partes[0]) continue;
                Transform t = root.transform;
                bool ok = true;
                for (int i = 1; i < partes.Length && ok; i++)
                {
                    Transform prox = null;
                    foreach (Transform f in t) if (f.name == partes[i]) { prox = f; break; }
                    if (prox == null) ok = false; else t = prox;
                }
                if (ok) return t.gameObject;
            }
            return null;
        }

        static string Goid(Object o)
        {
            if (o == null) return null;
            var id = GlobalObjectId.GetGlobalObjectIdSlow(o);
            if (id.identifierType == 0 || id.targetObjectId == 0) return null;
            return id.ToString();
        }

        static Object DeGoid(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            GlobalObjectId id;
            if (!GlobalObjectId.TryParse(s, out id)) return null;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
        }

        public static string NomeLimpo(string n)
        {
            if (string.IsNullOrEmpty(n)) return n;
            n = Regex.Replace(n, @"\s*\(Missing.*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s*\(\d+\)$", "");
            return n.Trim();
        }

        public static RefAsset Ref(Object o)
        {
            var r = new RefAsset { nome = o.name, tipo = o.GetType().Name, caminho = AssetDatabase.GetAssetPath(o) };
            string g; long l;
            if (!string.IsNullOrEmpty(r.caminho) && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out g, out l))
            {
                r.guid = g; r.localId = l;
            }
            return r;
        }

        static Mesh MeshDe(Renderer r)
        {
            var smr = r as SkinnedMeshRenderer;
            if (smr != null) return smr.sharedMesh;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        static bool EhRaizPrefab(GameObject go)
        {
            if (PrefabUtility.IsOutermostPrefabInstanceRoot(go)) return true;
            return EhRaizEmFalta(go);
        }

        static bool EhRaizEmFalta(GameObject go)
        {
            if (!PrefabUtility.IsPrefabAssetMissing(go)) return false;
            var raiz = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (raiz != null && raiz != go) return false;
            var pai = go.transform.parent;
            return pai == null || !PrefabUtility.IsPrefabAssetMissing(pai.gameObject);
        }

        // ------------------------------------------------------------------ shaders
        public static bool ShaderPartido(Material m)
        {
            return m == null || ShaderPartido(m.shader);
        }

        static bool ShaderPartido(Shader s)
        {
            if (s == null) return true;
            if (s.name == "Hidden/InternalErrorShader") return true;
            if (!s.isSupported) return true;
            if (GraphicsSettings.currentRenderPipeline != null && EhBuiltInIluminado(s.name)) return true;
            return false;
        }

        static bool EhBuiltInIluminado(string n)
        {
            return n == "Standard" || n == "Standard (Specular setup)" || n == "Autodesk Interactive"
                || (n.StartsWith("Legacy Shaders/") && !n.Contains("Particles"));
        }

        static Shader ShaderSubstituto(RegMaterial rec)
        {
            if (rec != null && !string.IsNullOrEmpty(rec.shader))
            {
                var s = Shader.Find(rec.shader);
                if (!ShaderPartido(s)) return s;
            }
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp != null)
            {
                string tn = rp.GetType().Name;
                Shader s = null;
                if (tn.Contains("Universal")) s = Shader.Find("Universal Render Pipeline/Lit");
                else if (tn.Contains("HD")) s = Shader.Find("HDRP/Lit");
                if (s != null) return s;
                try
                {
                    var p = rp.GetType().GetProperty("defaultMaterial", BindingFlags.Public | BindingFlags.Instance);
                    var dm = p != null ? p.GetValue(rp, null) as Material : null;
                    if (dm != null && dm.shader != null) return dm.shader;
                }
                catch (Exception) { }
                s = Shader.Find("Universal Render Pipeline/Lit");
                if (s != null) return s;
            }
            return Shader.Find("Standard");
        }

        // ------------------------------------------------------------------ materiais (captura / aplicação)
        static RegMaterial CapturarMaterial(Material m)
        {
            var r = new RegMaterial { refMat = Ref(m), shader = m.shader.name, renderQueue = m.renderQueue };
            r.keywords.AddRange(m.shaderKeywords);
            var sh = m.shader;
            int n = sh.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string pn = sh.GetPropertyName(i);
                try
                {
                    switch (sh.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color:
                            r.cores.Add(new PropCor { n = pn, v = m.GetColor(pn) }); break;
                        case ShaderPropertyType.Vector:
                            r.vecs.Add(new PropVec { n = pn, v = m.GetVector(pn) }); break;
                        case ShaderPropertyType.Texture:
                            var t = m.GetTexture(pn);
                            r.texs.Add(new PropTex
                            {
                                n = pn,
                                tex = t != null ? Ref(t) : new RefAsset(),
                                escala = m.GetTextureScale(pn),
                                offset = m.GetTextureOffset(pn)
                            });
                            break;
                        default:
                            r.floats.Add(new PropFloat { n = pn, v = m.GetFloat(pn) }); break;
                    }
                }
                catch (Exception) { }
            }
            return r;
        }

        /// <summary>Lê as propriedades guardadas no ficheiro do material (mesmo com o shader partido).</summary>
        static RegMaterial LerGuardadas(Material m)
        {
            var r = new RegMaterial();
            try
            {
                var so = new SerializedObject(m);
                var tex = so.FindProperty("m_SavedProperties.m_TexEnvs");
                if (tex != null && tex.isArray)
                    for (int i = 0; i < tex.arraySize; i++)
                    {
                        var el = tex.GetArrayElementAtIndex(i);
                        var nome = el.FindPropertyRelative("first");
                        var sec = el.FindPropertyRelative("second");
                        if (nome == null || sec == null) continue;
                        var t = sec.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
                        r.texs.Add(new PropTex
                        {
                            n = nome.stringValue,
                            tex = t != null ? Ref(t) : new RefAsset(),
                            escala = sec.FindPropertyRelative("m_Scale").vector2Value,
                            offset = sec.FindPropertyRelative("m_Offset").vector2Value
                        });
                    }
                var fl = so.FindProperty("m_SavedProperties.m_Floats");
                if (fl != null && fl.isArray)
                    for (int i = 0; i < fl.arraySize; i++)
                    {
                        var el = fl.GetArrayElementAtIndex(i);
                        var nome = el.FindPropertyRelative("first");
                        var sec = el.FindPropertyRelative("second");
                        if (nome != null && sec != null) r.floats.Add(new PropFloat { n = nome.stringValue, v = sec.floatValue });
                    }
                var co = so.FindProperty("m_SavedProperties.m_Colors");
                if (co != null && co.isArray)
                    for (int i = 0; i < co.arraySize; i++)
                    {
                        var el = co.GetArrayElementAtIndex(i);
                        var nome = el.FindPropertyRelative("first");
                        var sec = el.FindPropertyRelative("second");
                        if (nome != null && sec != null) r.cores.Add(new PropCor { n = nome.stringValue, v = sec.colorValue });
                    }
            }
            catch (Exception) { }
            return r;
        }

        static List<string> Alvos(Material m, string n)
        {
            var l = new List<string>();
            if (m.HasProperty(n)) l.Add(n);
            string[] a;
            if (Alias.TryGetValue(n, out a))
                foreach (var x in a) if (m.HasProperty(x) && !l.Contains(x)) l.Add(x);
            return l;
        }

        /// <returns>Número de propriedades alteradas.</returns>
        static int AplicarProps(Material m, RegMaterial r, bool soTexturasVazias)
        {
            int n = 0;
            if (!soTexturasVazias)
            {
                foreach (var c in r.cores)
                    foreach (var a in Alvos(m, c.n))
                        if (m.GetColor(a) != c.v) { m.SetColor(a, c.v); n++; }
                foreach (var f in r.floats)
                    foreach (var a in Alvos(m, f.n))
                        if (!Mathf.Approximately(m.GetFloat(a), f.v)) { m.SetFloat(a, f.v); n++; }
                foreach (var v in r.vecs)
                    foreach (var a in Alvos(m, v.n))
                        if (m.GetVector(a) != v.v) { m.SetVector(a, v.v); n++; }
            }
            foreach (var t in r.texs)
            {
                if (t.tex == null || t.tex.Vazio) continue;
                foreach (var a in Alvos(m, t.n))
                {
                    var atual = m.GetTexture(a);
                    if (soTexturasVazias && atual != null) continue;
                    var tx = Resolver<Texture>(t.tex);
                    if (tx == null || tx == atual) continue;
                    m.SetTexture(a, tx);
                    m.SetTextureScale(a, t.escala);
                    m.SetTextureOffset(a, t.offset);
                    n++;
                }
            }
            return n;
        }

        static void AjustarKeywords(Material m)
        {
            if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null) m.EnableKeyword("_NORMALMAP");
            if (m.HasProperty("_MetallicGlossMap") && m.GetTexture("_MetallicGlossMap") != null)
            {
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.EnableKeyword("_METALLICGLOSSMAP");
            }
            if (m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0.01f)
                m.EnableKeyword("_EMISSION");
        }

        static bool SoLeitura(Material m)
        {
            string p = AssetDatabase.GetAssetPath(m);
            if (string.IsNullOrEmpty(p)) return false; // material embutido na cena: editável
            if (!p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) return true; // dentro de FBX, etc.
            if (p.StartsWith("Packages/"))
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(p);
                return info == null || info.source != UnityEditor.PackageManager.PackageSource.Embedded;
            }
            return false;
        }

        // ------------------------------------------------------------------ resolver assets
        public static T Resolver<T>(RefAsset r) where T : Object
        {
            if (r == null || r.Vazio) return null;
            string k = typeof(T).Name + "|" + r.Chave;
            Object c;
            if (cache.TryGetValue(k, out c) && c != null) return c as T;
            T res = ResolverInterno<T>(r);
            cache[k] = res;
            return res;
        }

        static T ResolverInterno<T>(RefAsset r) where T : Object
        {
            if (!string.IsNullOrEmpty(r.caminho) &&
                (r.caminho.StartsWith("Resources/unity_builtin") || r.caminho.StartsWith("Library/")))
            {
                T b = Embutido<T>(r.nome);
                if (b != null) return b;
            }
            if (!string.IsNullOrEmpty(r.guid))
            {
                T a = DoFicheiro<T>(AssetDatabase.GUIDToAssetPath(r.guid), r);
                if (a != null) return a;
            }
            if (!string.IsNullOrEmpty(r.caminho))
            {
                T a = DoFicheiro<T>(r.caminho, r);
                if (a != null) return a;
            }
            return ProcurarPorNome<T>(r.nome, r.caminho, r.tipo);
        }

        static T DoFicheiro<T>(string p, RefAsset r) where T : Object
        {
            if (string.IsNullOrEmpty(p) || AssetDatabase.LoadMainAssetAtPath(p) == null) return null;
            var todos = AssetDatabase.LoadAllAssetsAtPath(p);
            if (todos == null || todos.Length == 0) return null;
            if (r.localId != 0)
                foreach (var o in todos)
                {
                    if (!(o is T)) continue;
                    string g; long l;
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out g, out l) && l == r.localId) return (T)o;
                }
            foreach (var o in todos) if (o is T && o.name == r.nome) return (T)o;
            if (typeof(T) == typeof(GameObject)) return AssetDatabase.LoadAssetAtPath<GameObject>(p) as T;
            return null;
        }

        static T Embutido<T>(string nome) where T : Object
        {
            string[] ext = typeof(T) == typeof(Mesh) ? new[] { ".fbx", "" }
                         : typeof(T) == typeof(Material) ? new[] { ".mat", "" }
                         : new[] { "" };
            foreach (var e in ext)
            {
                try { var x = AssetDatabase.GetBuiltinExtraResource<T>(nome + e); if (x != null) return x; } catch (Exception) { }
                try { var x = Resources.GetBuiltinResource<T>(nome + e); if (x != null) return x; } catch (Exception) { }
            }
            return null;
        }

        public static T ProcurarPorNome<T>(string nome, string caminhoOriginal, string tipo) where T : Object
        {
            if (string.IsNullOrEmpty(nome)) return null;
            bool ehGO = typeof(T) == typeof(GameObject);
            string filtro = ehGO ? "t:GameObject" : "t:" + typeof(T).Name;
            string pastaOrig = "";
            if (!string.IsNullOrEmpty(caminhoOriginal))
                pastaOrig = (Path.GetDirectoryName(caminhoOriginal) ?? "").Replace('\\', '/');

            var candidatos = new List<KeyValuePair<int, T>>();
            var vistos = new HashSet<string>();
            string[] pesquisas = { nome + " " + filtro, filtro };
            foreach (string pesquisa in pesquisas)
            {
                bool segunda = pesquisa == filtro;
                string[] guids;
                try { guids = AssetDatabase.FindAssets(pesquisa); } catch (Exception) { continue; }
                foreach (var g in guids)
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    if (string.IsNullOrEmpty(p) || !vistos.Add(p)) continue;
                    if (p.StartsWith(Caminhos.PastaRecuperados + "/Temp")) continue;
                    string fn = Path.GetFileNameWithoutExtension(p);
                    bool mesmoFicheiro = string.Equals(fn, nome, StringComparison.OrdinalIgnoreCase);
                    if (segunda && !mesmoFicheiro) continue;

                    T achado = null;
                    if (ehGO)
                    {
                        if (mesmoFicheiro) achado = AssetDatabase.LoadAssetAtPath<GameObject>(p) as T;
                    }
                    else
                    {
                        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                            if (o is T && o.name == nome) { achado = (T)o; break; }
                    }
                    if (achado == null) continue;

                    int pts = 0;
                    if (mesmoFicheiro) pts += 2;
                    string pasta = (Path.GetDirectoryName(p) ?? "").Replace('\\', '/');
                    if (pastaOrig != "" && pasta == pastaOrig) pts += 4;
                    if (ehGO && tipo == "Prefab" && p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) pts += 3;
                    if (ehGO && tipo == "Model" && !p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) pts += 3;
                    if (p.StartsWith(Caminhos.PastaRecuperados)) pts -= 1;
                    candidatos.Add(new KeyValuePair<int, T>(pts, achado));
                }
                if (candidatos.Count > 0) break;
            }
            return candidatos.OrderByDescending(c => c.Key).Select(c => c.Value).FirstOrDefault();
        }

        // ------------------------------------------------------------------ SNAPSHOT
        public static void TirarSnapshot(Scene cena, bool estadoBom)
        {
            if (!cena.IsValid() || !cena.isLoaded || string.IsNullOrEmpty(cena.path)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                var snap = Ler(cena) ?? new SnapshotCena();
                snap.cenaGuid = AssetDatabase.AssetPathToGUID(cena.path);
                snap.cenaCaminho = cena.path;
                snap.atualizado = Caminhos.Agora();
                if (estadoBom) snap.estadoBomEm = Caminhos.Agora();

                // --- Renderers
                var regs = new Dictionary<string, RegRenderer>();
                foreach (var r in snap.renderers) if (!string.IsNullOrEmpty(r.goid)) regs[r.goid] = r;
                var presentes = new HashSet<string>();
                var usados = new HashSet<Material>();
                var usadosBom = new HashSet<Material>();

                foreach (var rend in TodosRenderers(cena))
                {
                    string id = Goid(rend);
                    if (id == null) continue;
                    presentes.Add(id);
                    RegRenderer reg;
                    if (!regs.TryGetValue(id, out reg)) { reg = new RegRenderer { goid = id }; regs[id] = reg; }
                    reg.caminhoHierarquia = CaminhoHierarquia(rend.transform);
                    reg.tipoComp = rend.GetType().Name;

                    var sm = rend.sharedMaterials;
                    bool todosOk = true;
                    for (int i = 0; i < sm.Length; i++)
                    {
                        while (reg.materiais.Count <= i) reg.materiais.Add(new RefAsset());
                        var m = sm[i];
                        if (m != null && !ShaderPartido(m)) { reg.materiais[i] = Ref(m); usados.Add(m); }
                        else todosOk = false; // nunca substituir informação boa por vazia
                    }
                    if (todosOk && reg.materiais.Count > sm.Length)
                        reg.materiais.RemoveRange(sm.Length, reg.materiais.Count - sm.Length);
                    if (estadoBom && todosOk)
                    {
                        reg.materiaisBom = sm.Select(m => Ref(m)).ToList();
                        foreach (var m in sm) usadosBom.Add(m);
                    }

                    var mesh = MeshDe(rend);
                    if (mesh != null) reg.mesh = Ref(mesh);
                }
                snap.renderers = regs.Values.Where(r => presentes.Contains(r.goid)).ToList();

                // --- Skybox (só a cena ativa)
                if (cena == SceneManager.GetActiveScene() && RenderSettings.skybox != null && !ShaderPartido(RenderSettings.skybox))
                {
                    snap.skybox = Ref(RenderSettings.skybox);
                    usados.Add(RenderSettings.skybox);
                    if (estadoBom) usadosBom.Add(RenderSettings.skybox);
                }

                // --- Materiais
                var mats = new Dictionary<string, RegMaterial>();
                foreach (var m in snap.materiais) if (m.refMat != null) mats[m.refMat.Chave] = m;
                foreach (var m in usados)
                {
                    var cap = CapturarMaterial(m);
                    mats[cap.refMat.Chave] = cap;
                }
                var referidos = new HashSet<string>();
                foreach (var r in snap.renderers)
                {
                    foreach (var x in r.materiais) if (x != null && !x.Vazio) referidos.Add(x.Chave);
                    foreach (var x in r.materiaisBom) if (x != null && !x.Vazio) referidos.Add(x.Chave);
                }
                if (snap.skybox != null && !snap.skybox.Vazio) referidos.Add(snap.skybox.Chave);
                snap.materiais = mats.Values.Where(m => referidos.Contains(m.refMat.Chave)).ToList();
                if (estadoBom) snap.materiaisBom = usadosBom.Where(m => m != null).Select(CapturarMaterial).ToList();

                // --- Prefabs
                var pres = new Dictionary<string, RegPrefab>();
                foreach (var p in snap.prefabs) if (!string.IsNullOrEmpty(p.goid)) pres[p.goid] = p;
                var presentesP = new HashSet<string>();
                foreach (var go in TodosGO(cena))
                {
                    if (!EhRaizPrefab(go)) continue;
                    string id = Goid(go);
                    if (id == null) continue;
                    presentesP.Add(id);
                    RegPrefab reg;
                    if (!pres.TryGetValue(id, out reg)) { reg = new RegPrefab { goid = id }; pres[id] = reg; }
                    var t = go.transform;
                    reg.nome = NomeLimpo(go.name);
                    reg.caminhoHierarquia = CaminhoHierarquia(t);
                    reg.pos = t.localPosition; reg.rot = t.localRotation; reg.escala = t.localScale;
                    reg.irmao = t.GetSiblingIndex();
                    reg.paiGoid = t.parent != null ? Goid(t.parent.gameObject) : "";
                    reg.paiCaminho = t.parent != null ? CaminhoHierarquia(t.parent) : "";
                    if (!PrefabUtility.IsPrefabAssetMissing(go))
                    {
                        var fonte = PrefabUtility.GetCorrespondingObjectFromSource(go);
                        string ap = fonte != null ? AssetDatabase.GetAssetPath(fonte) : null;
                        var asset = string.IsNullOrEmpty(ap) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(ap);
                        if (asset != null)
                        {
                            reg.prefab = Ref(asset);
                            reg.prefab.nome = Path.GetFileNameWithoutExtension(ap);
                            reg.prefab.tipo = ap.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? "Prefab" : "Model";
                        }
                    }
                    if (estadoBom) reg.noEstadoBom = true;
                }
                snap.prefabs = estadoBom
                    ? pres.Values.Where(p => presentesP.Contains(p.goid)).ToList()
                    : pres.Values.ToList();

                Gravar(cena, snap);
                if (estadoBom) Debug.Log(Caminhos.Log + "Estado bom guardado para \"" + cena.name + "\" (" +
                                         snap.renderers.Count + " renderers, " + snap.prefabs.Count + " prefabs, " +
                                         snap.materiais.Count + " materiais).");
            }
            catch (Exception e)
            {
                Debug.LogWarning(Caminhos.Log + "Não foi possível tirar o snapshot de \"" + cena.name + "\": " + e.Message);
            }
        }

        public static void GuardarEstadoBom()
        {
            int n = 0;
            foreach (var c in CenasAbertas())
            {
                if (string.IsNullOrEmpty(c.path)) continue;
                TirarSnapshot(c, true);
                n++;
            }
            if (n == 0)
                EditorUtility.DisplayDialog("VirtuALLme · Estado bom", "Guarda a cena primeiro (Ctrl+S) — uma cena sem nome não tem onde registar o estado.", "OK");
            else
                EditorUtility.DisplayDialog("VirtuALLme · Estado bom",
                    "Estado bom registado para " + n + " cena(s).\n\nSe alguma coisa se perder, o botão \"Reparar cena\" volta a deixá-la assim.", "OK");
        }

        // ------------------------------------------------------------------ diagnóstico
        public static int ContarProblemas(Scene cena)
        {
            int n = 0;
            foreach (var go in TodosGO(cena))
                if (PrefabUtility.IsPrefabAssetMissing(go) && EhRaizEmFalta(go)) n++;
            foreach (var r in TodosRenderers(cena))
            {
                var so = new SerializedObject(r);
                var arr = so.FindProperty("m_Materials");
                if (arr != null && arr.isArray)
                    for (int i = 0; i < arr.arraySize; i++)
                    {
                        var el = arr.GetArrayElementAtIndex(i);
                        if (el.objectReferenceValue == null && el.objectReferenceInstanceIDValue != 0) n++;
                    }
                foreach (var m in r.sharedMaterials)
                    if (m != null && ShaderPartido(m)) n++;
                var smr = r as SkinnedMeshRenderer;
                var mf = r.GetComponent<MeshFilter>();
                if ((smr != null && smr.sharedMesh == null) || (smr == null && mf != null && mf.sharedMesh == null)) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ REPARAR
        public static void Reparar(bool interativo = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("VirtuALLme · Reparar cena", "Sai do Play Mode primeiro.", "OK");
                return;
            }
            AssetDatabase.Refresh();
            cache.Clear();

            var cfg = SegurancaConfig.I;
            var cenas = CenasAbertas().ToList();
            var snaps = cenas.Select(c => Ler(c)).ToList();

            // objetos do "estado bom" que desapareceram
            var apagados = new List<string>();
            for (int i = 0; i < cenas.Count; i++)
            {
                if (snaps[i] == null) continue;
                foreach (var p in snaps[i].prefabs)
                    if (p.noEstadoBom && !Presente(cenas[i], p)) apagados.Add(p.caminhoHierarquia);
            }

            bool recriar = false;
            if (apagados.Count > 0 && interativo)
            {
                string lista = string.Join("\n", apagados.Take(12).Select(a => "• " + a).ToArray());
                if (apagados.Count > 12) lista += "\n… e mais " + (apagados.Count - 12);
                int op = EditorUtility.DisplayDialogComplex("VirtuALLme · Reparar cena",
                    apagados.Count + " objeto(s) que estavam no último \"estado bom\" já não estão na cena:\n\n" + lista +
                    "\n\nQueres recriá-los no mesmo sítio?",
                    "Reparar e recriar", "Cancelar", "Reparar sem recriar");
                if (op == 1) return;
                recriar = op == 0;
            }

            Undo.IncrementCurrentGroup();
            int grupo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("VirtuALLme: Reparar cena");
            var rel = new Relatorio();
            try
            {
                for (int i = 0; i < cenas.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("VirtuALLme · Reparar cena", cenas[i].name, (float)i / cenas.Count);
                    RepararCena(cenas[i], snaps[i], recriar, cfg.modoForte, rel);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                rel.naoResolvidos.Add("Erro: " + e.Message);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Undo.CollapseUndoOperations(grupo);
            }
            AssetDatabase.SaveAssets();

            UltimoRelatorio = Caminhos.Agora() + "\n" + rel.Texto();
            foreach (var l in rel.linhas) Debug.Log(Caminhos.Log + l);
            foreach (var l in rel.naoResolvidos) Debug.LogWarning(Caminhos.Log + l);
            Debug.Log(Caminhos.Log + "Reparação concluída:\n" + rel.Texto());

            if (!interativo) return;
            if (rel.Total == 0)
            {
                EditorUtility.DisplayDialog("VirtuALLme · Reparar cena",
                    "Está tudo certinho — não encontrei nada para reparar.\n\n" + rel.Avisos(), "OK");
            }
            else if (EditorUtility.DisplayDialog("VirtuALLme · Reparar cena",
                         "Cena reparada!\n\n" + rel.Texto() + "\n\n(Ctrl+Z desfaz tudo.) Guardar a cena agora?",
                         "Guardar agora", "Mais tarde"))
            {
                EditorSceneManager.SaveOpenScenes();
            }
        }

        static bool Presente(Scene cena, RegPrefab reg)
        {
            var o = DeGoid(reg.goid) as GameObject;
            if (o != null && o.scene == cena) return true;
            return AcharPorCaminho(cena, reg.caminhoHierarquia) != null;
        }

        static RegPrefab AcharRegPrefab(SnapshotCena snap, GameObject go)
        {
            if (snap == null) return null;
            string id = Goid(go);
            string caminho = CaminhoHierarquia(go.transform);
            string pai = go.transform.parent != null ? CaminhoHierarquia(go.transform.parent) : "";
            string nome = NomeLimpo(go.name);
            RegPrefab r = null;
            if (id != null) r = snap.prefabs.FirstOrDefault(p => p.goid == id && p.prefab != null && !p.prefab.Vazio);
            if (r == null) r = snap.prefabs.FirstOrDefault(p => p.caminhoHierarquia == caminho && p.prefab != null && !p.prefab.Vazio);
            if (r == null) r = snap.prefabs.FirstOrDefault(p => (p.paiCaminho ?? "") == pai && p.nome == nome && p.prefab != null && !p.prefab.Vazio);
            return r;
        }

        static Transform ResolverPai(Scene cena, RegPrefab reg)
        {
            var o = DeGoid(reg.paiGoid) as GameObject;
            if (o != null && o.scene == cena) return o.transform;
            var g = AcharPorCaminho(cena, reg.paiCaminho);
            return g != null ? g.transform : null;
        }

        static bool TentarReplace(GameObject go, GameObject asset)
        {
            try
            {
                var m = typeof(PrefabUtility).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(x => x.Name == "ReplacePrefabAssetOfPrefabInstance"
                                         && x.GetParameters().Length == 3
                                         && x.GetParameters()[2].ParameterType == typeof(InteractionMode));
                if (m == null) return false;
                m.Invoke(null, new object[] { go, asset, InteractionMode.UserAction });
                return go != null && !PrefabUtility.IsPrefabAssetMissing(go);
            }
            catch (Exception) { return false; }
        }

        static GameObject InstanciarNoLugar(GameObject asset, GameObject antigo, RegPrefab reg, Scene cena)
        {
            var novo = PrefabUtility.InstantiatePrefab(asset, cena) as GameObject;
            if (novo == null) return null;
            Undo.RegisterCreatedObjectUndo(novo, "Recriar prefab");

            Transform pai; Vector3 lp, ls; Quaternion lr; int irmao; string nome;
            if (antigo != null)
            {
                var t = antigo.transform;
                pai = t.parent; lp = t.localPosition; lr = t.localRotation; ls = t.localScale;
                irmao = t.GetSiblingIndex(); nome = NomeLimpo(antigo.name);
            }
            else
            {
                pai = ResolverPai(cena, reg); lp = reg.pos; lr = reg.rot; ls = reg.escala;
                irmao = reg.irmao; nome = reg.nome;
            }
            if (pai != null) Undo.SetTransformParent(novo.transform, pai, "Recriar prefab");
            novo.transform.localPosition = lp;
            novo.transform.localRotation = lr;
            if (ls != Vector3.zero) novo.transform.localScale = ls;
            novo.transform.SetSiblingIndex(irmao);
            if (!string.IsNullOrEmpty(nome)) novo.name = nome;

            if (antigo != null)
            {
                var filhos = new List<Transform>();
                foreach (Transform f in antigo.transform) filhos.Add(f);
                foreach (var f in filhos)
                {
                    try
                    {
                        var g = f.gameObject;
                        bool adicionado = !PrefabUtility.IsPartOfPrefabInstance(g)
                                          || PrefabUtility.IsAddedGameObjectOverride(g)
                                          || PrefabUtility.IsOutermostPrefabInstanceRoot(g);
                        if (adicionado) Undo.SetTransformParent(f, novo.transform, "Recriar prefab");
                    }
                    catch (Exception) { }
                }
                Undo.DestroyObjectImmediate(antigo);
            }
            return novo;
        }

        static Material RecriarMaterial(SnapshotCena snap, RefAsset refMat, Relatorio rel)
        {
            if (snap == null || refMat == null || refMat.Vazio) return null;
            var rec = snap.materiaisBom.Concat(snap.materiais).FirstOrDefault(m => m.refMat != null && m.refMat.Chave == refMat.Chave)
                   ?? snap.materiaisBom.Concat(snap.materiais).FirstOrDefault(m => m.refMat != null && m.refMat.nome == refMat.nome);
            if (rec == null) return null;

            var sh = Shader.Find(rec.shader);
            bool mesmoShader = !ShaderPartido(sh);
            if (!mesmoShader) sh = ShaderSubstituto(null);
            if (sh == null) return null;

            var m = new Material(sh) { name = rec.refMat.nome };
            AplicarProps(m, rec, false);
            if (mesmoShader) { m.shaderKeywords = rec.keywords.ToArray(); m.renderQueue = rec.renderQueue; }
            else AjustarKeywords(m);

            string destino = rec.refMat.caminho;
            bool caminhoOk = !string.IsNullOrEmpty(destino) && destino.StartsWith("Assets/")
                             && destino.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)
                             && AssetDatabase.IsValidFolder(Path.GetDirectoryName(destino).Replace('\\', '/'))
                             && AssetDatabase.LoadMainAssetAtPath(destino) == null;
            if (!caminhoOk)
            {
                Caminhos.GarantirPastaAssets(Caminhos.PastaRecuperados + "/Materiais");
                destino = AssetDatabase.GenerateUniqueAssetPath(Caminhos.PastaRecuperados + "/Materiais/" + Caminhos.NomeSeguro(rec.refMat.nome) + ".mat");
            }
            AssetDatabase.CreateAsset(m, destino);

            // devolver o GUID antigo → todas as referências do projeto voltam a funcionar
            if (SegurancaConfig.I.restaurarGuidOriginal && !string.IsNullOrEmpty(rec.refMat.guid)
                && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(rec.refMat.guid)))
            {
                try
                {
                    string meta = Caminhos.Abs(destino + ".meta");
                    string novoGuid = AssetDatabase.AssetPathToGUID(destino);
                    if (File.Exists(meta) && !string.IsNullOrEmpty(novoGuid))
                    {
                        File.WriteAllText(meta, File.ReadAllText(meta).Replace(novoGuid, rec.refMat.guid));
                        AssetDatabase.ImportAsset(destino, ImportAssetOptions.ForceUpdate);
                        var rec2 = AssetDatabase.LoadAssetAtPath<Material>(destino);
                        if (rec2 != null) m = rec2;
                    }
                }
                catch (Exception e) { Debug.LogWarning(Caminhos.Log + "Não consegui repor o GUID original: " + e.Message); }
            }

            cache["Material|" + refMat.Chave] = m;
            rel.matsRecriados++;
            rel.linhas.Add("Material recriado: " + destino);
            return m;
        }

        static RegMaterial AcharRegMaterial(List<RegMaterial> lista, Material m)
        {
            if (lista == null || m == null) return null;
            string k = Ref(m).Chave;
            return lista.FirstOrDefault(x => x.refMat != null && x.refMat.Chave == k);
        }

        static void RepararCena(Scene cena, SnapshotCena snap, bool recriar, bool forte, Relatorio rel)
        {
            int antes = rel.Total;

            // 1) Prefabs "Missing Prefab"
            var emFalta = TodosGO(cena).Where(g => PrefabUtility.IsPrefabAssetMissing(g) && EhRaizEmFalta(g)).ToList();
            foreach (var go in emFalta)
            {
                if (go == null) continue;
                var reg = AcharRegPrefab(snap, go);
                GameObject asset = reg != null ? Resolver<GameObject>(reg.prefab) : null;
                if (asset == null)
                {
                    string nome = reg != null && !string.IsNullOrEmpty(reg.prefab.nome) ? reg.prefab.nome : NomeLimpo(go.name);
                    asset = ProcurarPorNome<GameObject>(nome,
                        reg != null ? reg.prefab.caminho : null, reg != null ? reg.prefab.tipo : null);
                }
                string caminho = CaminhoHierarquia(go.transform);
                if (asset == null) { rel.naoResolvidos.Add("Prefab em falta sem substituto: " + caminho); continue; }

                GameObject novo = TentarReplace(go, asset) ? go : null;
                if (novo == null && go != null) novo = InstanciarNoLugar(asset, go, reg, cena);
                if (novo != null)
                {
                    rel.prefabs++;
                    rel.linhas.Add("Prefab reposto: " + caminho + "  ←  " + AssetDatabase.GetAssetPath(asset));
                }
            }

            // 2) Objetos apagados desde o "estado bom"
            if (recriar && snap != null)
            {
                foreach (var reg in snap.prefabs.Where(p => p.noEstadoBom)
                             .OrderBy(p => p.caminhoHierarquia.Count(ch => ch == '/')).ToList())
                {
                    if (Presente(cena, reg)) continue;
                    var asset = Resolver<GameObject>(reg.prefab);
                    if (asset == null) { rel.naoResolvidos.Add("Não encontrei o prefab de " + reg.caminhoHierarquia); continue; }
                    if (InstanciarNoLugar(asset, null, reg, cena) != null)
                    {
                        rel.recriados++;
                        rel.linhas.Add("Recriado: " + reg.caminhoHierarquia);
                    }
                }
            }

            // 3) Materiais e meshes nos Renderers
            var renderers = TodosRenderers(cena).ToList();
            var comRegisto = new HashSet<Renderer>();
            if (snap != null)
            {
                var porId = new Dictionary<string, Renderer>();
                var porCaminho = new Dictionary<string, Renderer>();
                foreach (var r in renderers)
                {
                    string id = Goid(r);
                    if (id != null) porId[id] = r;
                    string k = CaminhoHierarquia(r.transform) + "|" + r.GetType().Name;
                    if (!porCaminho.ContainsKey(k)) porCaminho[k] = r;
                }
                foreach (var reg in snap.renderers)
                {
                    Renderer r;
                    if (!porId.TryGetValue(reg.goid, out r))
                        porCaminho.TryGetValue(reg.caminhoHierarquia + "|" + reg.tipoComp, out r);
                    if (r == null) continue;
                    comRegisto.Add(r);

                    var fonte = forte && reg.materiaisBom != null && reg.materiaisBom.Count > 0 ? reg.materiaisBom : reg.materiais;
                    var sm = r.sharedMaterials;
                    bool alterou = false;
                    for (int i = 0; i < sm.Length && i < fonte.Count; i++)
                    {
                        var alvo = fonte[i];
                        if (alvo == null || alvo.Vazio) continue;
                        bool precisa = sm[i] == null || (forte && Ref(sm[i]).Chave != alvo.Chave);
                        if (!precisa) continue;
                        var m = Resolver<Material>(alvo) ?? RecriarMaterial(snap, alvo, rel);
                        if (m == null) { rel.naoResolvidos.Add("Material \"" + alvo.nome + "\" em " + reg.caminhoHierarquia); continue; }
                        if (m == sm[i]) continue;
                        sm[i] = m; alterou = true; rel.materiais++;
                    }
                    if (alterou)
                    {
                        Undo.RecordObject(r, "Repor materiais");
                        r.sharedMaterials = sm;
                        rel.linhas.Add("Materiais repostos: " + reg.caminhoHierarquia);
                    }

                    if (reg.mesh != null && !reg.mesh.Vazio)
                    {
                        var smr = r as SkinnedMeshRenderer;
                        var mf = r.GetComponent<MeshFilter>();
                        Mesh atual = smr != null ? smr.sharedMesh : (mf != null ? mf.sharedMesh : null);
                        if (atual == null && (smr != null || mf != null))
                        {
                            var mesh = Resolver<Mesh>(reg.mesh);
                            if (mesh != null)
                            {
                                if (smr != null) { Undo.RecordObject(smr, "Repor mesh"); smr.sharedMesh = mesh; }
                                else { Undo.RecordObject(mf, "Repor mesh"); mf.sharedMesh = mesh; }
                                var mc = r.GetComponent<MeshCollider>();
                                if (mc != null && mc.sharedMesh == null) { Undo.RecordObject(mc, "Repor mesh"); mc.sharedMesh = mesh; }
                                rel.meshes++;
                                rel.linhas.Add("Mesh reposta: " + reg.caminhoHierarquia + " ← " + mesh.name);
                            }
                            else rel.naoResolvidos.Add("Mesh \"" + reg.mesh.nome + "\" em " + reg.caminhoHierarquia);
                        }
                    }
                }
            }
            foreach (var r in renderers)
                if (!comRegisto.Contains(r))
                    foreach (var m in r.sharedMaterials) if (m == null) rel.slotsSemInfo++;

            // 4) Skybox
            if (snap != null && cena == SceneManager.GetActiveScene() && RenderSettings.skybox == null
                && snap.skybox != null && !snap.skybox.Vazio)
            {
                var sky = Resolver<Material>(snap.skybox) ?? RecriarMaterial(snap, snap.skybox, rel);
                if (sky != null) { RenderSettings.skybox = sky; rel.skybox++; }
            }

            // 5) Materiais em uso: shaders partidos (rosa), texturas perdidas, cores (modo forte)
            var emUso = new HashSet<Material>();
            foreach (var r in renderers) foreach (var m in r.sharedMaterials) if (m != null) emUso.Add(m);
            if (cena == SceneManager.GetActiveScene() && RenderSettings.skybox != null) emUso.Add(RenderSettings.skybox);
            var subst = new Dictionary<Material, Material>();

            foreach (var m in emUso)
            {
                var rec = snap != null ? AcharRegMaterial(snap.materiais, m) : null;
                var recBom = forte && snap != null ? AcharRegMaterial(snap.materiaisBom, m) : null;

                if (ShaderPartido(m))
                {
                    Material alvo = m;
                    if (SoLeitura(m))
                    {
                        alvo = new Material(m) { name = m.name };
                        Caminhos.GarantirPastaAssets(Caminhos.PastaRecuperados + "/Materiais");
                        string p = AssetDatabase.GenerateUniqueAssetPath(Caminhos.PastaRecuperados + "/Materiais/" + Caminhos.NomeSeguro(m.name) + ".mat");
                        AssetDatabase.CreateAsset(alvo, p);
                        subst[m] = alvo;
                        rel.matsRecriados++;
                        rel.linhas.Add("Material só-de-leitura extraído para " + p);
                    }
                    else Undo.RecordObject(alvo, "Corrigir shader");

                    var antigas = LerGuardadas(m);
                    string shaderAntes = m.shader != null ? m.shader.name : "(nenhum)";
                    alvo.shader = ShaderSubstituto(recBom ?? rec);
                    AplicarProps(alvo, antigas, false);
                    var fonteShader = recBom ?? rec;
                    if (fonteShader != null) AplicarProps(alvo, fonteShader, false);
                    AjustarKeywords(alvo);
                    EditorUtility.SetDirty(alvo);
                    rel.shaders++;
                    rel.linhas.Add("Shader corrigido em \"" + m.name + "\": " + shaderAntes + " → " + alvo.shader.name);
                }
                else if (!SoLeitura(m))
                {
                    var fonte = recBom ?? rec;
                    if (fonte == null) continue;
                    Undo.RecordObject(m, "Repor propriedades");
                    int n = AplicarProps(m, fonte, !forte);
                    if (forte && fonte.shader == m.shader.name)
                    {
                        var kw = fonte.keywords.ToArray();
                        if (!kw.SequenceEqual(m.shaderKeywords)) { m.shaderKeywords = kw; n++; }
                        if (m.renderQueue != fonte.renderQueue) { m.renderQueue = fonte.renderQueue; n++; }
                    }
                    if (n > 0)
                    {
                        EditorUtility.SetDirty(m);
                        rel.props += n;
                        rel.linhas.Add("Propriedades repostas em \"" + m.name + "\": " + n);
                    }
                }
            }
            if (subst.Count > 0)
            {
                foreach (var r in renderers)
                {
                    var sm = r.sharedMaterials;
                    bool mudou = false;
                    for (int i = 0; i < sm.Length; i++)
                    {
                        Material nm;
                        if (sm[i] != null && subst.TryGetValue(sm[i], out nm)) { sm[i] = nm; mudou = true; }
                    }
                    if (mudou) { Undo.RecordObject(r, "Usar material extraído"); r.sharedMaterials = sm; }
                }
                Material ns;
                if (RenderSettings.skybox != null && subst.TryGetValue(RenderSettings.skybox, out ns)) RenderSettings.skybox = ns;
            }

            // 6) Scripts em falta (só contar)
            foreach (var go in TodosGO(cena))
                rel.scriptsEmFalta += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);

            if (rel.Total != antes) EditorSceneManager.MarkSceneDirty(cena);
        }

        public static void RemoverScriptsEmFalta()
        {
            int n = 0;
            foreach (var c in CenasAbertas())
            {
                foreach (var go in TodosGO(c).ToList())
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) == 0) continue;
                    Undo.RegisterCompleteObjectUndo(go, "Remover scripts em falta");
                    n += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                }
                if (n > 0) EditorSceneManager.MarkSceneDirty(c);
            }
            EditorUtility.DisplayDialog("VirtuALLme", n == 0 ? "Não há scripts em falta." : "Removidos " + n + " componente(s) com script em falta.\n(Ctrl+Z desfaz.)", "OK");
        }
    }

    // Snapshots automáticos ao guardar / abrir cenas
    [InitializeOnLoad]
    static class ReparadorHooks
    {
        static ReparadorHooks()
        {
            EditorSceneManager.sceneSaved += AoGuardar;
            EditorSceneManager.sceneOpened += AoAbrir;
        }

        static void AoGuardar(Scene s)
        {
            if (AutoSalvamento.AGuardarCopia || !SegurancaConfig.I.snapshotAutomatico) return;
            ReparadorDeCena.TirarSnapshot(s, false);
        }

        static void AoAbrir(Scene s, OpenSceneMode modo)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AutoSalvamento.ARestaurar) return;
            var cfg = SegurancaConfig.I;
            EditorApplication.delayCall += () =>
            {
                if (!s.IsValid() || !s.isLoaded || string.IsNullOrEmpty(s.path)) return;
                int problemas = ReparadorDeCena.ContarProblemas(s);
                bool primeiro = !ReparadorDeCena.TemSnapshot(s);
                if (cfg.snapshotAutomatico) ReparadorDeCena.TirarSnapshot(s, primeiro && problemas == 0);
                if (problemas > 0)
                {
                    Debug.LogWarning(Caminhos.Log + "A cena \"" + s.name + "\" tem " + problemas +
                                     " problema(s) (materiais/prefabs/meshes perdidos ou shaders rosa). Menu VirtuALLme ▸ Segurança ▸ Reparar cena agora.");
                    if (cfg.avisarAoAbrirCena && EditorUtility.DisplayDialog("VirtuALLme · Cena com problemas",
                            "A cena \"" + s.name + "\" abriu com " + problemas +
                            " problema(s): materiais/prefabs/meshes perdidos ou shaders rosa.\n\nReparar agora?",
                            "Reparar agora", "Depois"))
                        ReparadorDeCena.Reparar(true);
                }
            };
        }
    }
}
