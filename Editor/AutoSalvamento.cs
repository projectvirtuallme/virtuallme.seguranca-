// VirtuALLme · Segurança do Projeto
// FERRAMENTA 2 — AUTO-SALVAMENTO E RECUPERAÇÃO APÓS CRASH
//
// • De X em X minutos (3 por defeito), antes de cada Play e antes de cada recompilação,
//   guarda uma CÓPIA de cada cena com alterações por guardar — sem mexer na cena original
//   (não precisas de Ctrl+S, e o Ctrl+Z continua a funcionar).
// • As cópias ficam em  <Projeto>/VirtuALLme_Seguranca/Backups/<Cena>_<id>/aaaaMMdd_HHmmss.unity
//   (fora de Assets e fora de Temp, por isso sobrevivem a crashes, reinícios e à limpeza do Unity).
// • Se o Unity travar / o PC reiniciar, na próxima abertura deteta que a sessão não fechou bem
//   e abre a janela "Recuperar versão" com as cópias mais recentes do que o ficheiro guardado.
// • Restaurar: faz primeiro cópia do estado atual (nada se perde) e depois repõe a versão escolhida.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VirtuALLme.Seguranca
{
    [Serializable]
    public class MetaBackup
    {
        public string cenaOriginal;
        public string nome;
        public string motivo;
        public string data;
        public string unity;
    }

    public class InfoBackup
    {
        public string ficheiro;
        public string cenaOriginal;
        public string nome;
        public string motivo;
        public DateTime data;
        public long bytes;
        public bool maisRecenteQueOriginal;
    }

    [InitializeOnLoad]
    public static class AutoSalvamento
    {
        public static bool AGuardarCopia;
        public static bool ARestaurar;

        const string KSessao = "VirtuALLme.Seguranca.SessaoAtiva";
        const string KUltimo = "VirtuALLme.Seguranca.UltimoBackup";
        const string KTempFalha = "VirtuALLme.Seguranca.TempFalhou";
        static double proximo;
        static double proximoBatimento;

        static string FicheiroFlag { get { return Path.Combine(Caminhos.RaizProjeto, "Library", "VirtuALLme_sessao.flag"); } }

        public static DateTime UltimoBackup
        {
            get
            {
                DateTime d;
                return DateTime.TryParse(SessionState.GetString(KUltimo, ""), out d) ? d : DateTime.MinValue;
            }
            private set { SessionState.SetString(KUltimo, value.ToString("o")); }
        }

        static AutoSalvamento()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += AoMudarPlay;
            AssemblyReloadEvents.beforeAssemblyReload += AntesDeRecompilar;
            EditorApplication.quitting += AoSair;

            proximo = EditorApplication.timeSinceStartup + Math.Max(0.5f, SegurancaConfig.I.intervaloMinutos) * 60.0;

            if (!SessionState.GetBool(KSessao, false))
            {
                SessionState.SetBool(KSessao, true);
                InicioDeSessao();
            }
        }

        // ------------------------------------------------------------------ deteção de crash
        static void InicioDeSessao()
        {
            string anterior = null;
            try { if (File.Exists(FicheiroFlag)) anterior = File.ReadAllText(FicheiroFlag); } catch (Exception) { }
            Batimento(true);

            if (anterior == null) return;
            Debug.LogWarning(Caminhos.Log + "A última sessão do Unity não fechou normalmente (crash, travamento ou reinício).");
            if (!SegurancaConfig.I.mostrarAvisoCrash) return;

            EditorApplication.delayCall += () =>
            {
                var recentes = ListarBackups().Where(b => b.maisRecenteQueOriginal).ToList();
                string quando = "";
                foreach (var linha in anterior.Split('\n'))
                    if (linha.StartsWith("ultimo=")) quando = linha.Substring(7).Trim();
                string aviso = "O Unity não fechou bem da última vez" +
                               (string.IsNullOrEmpty(quando) ? "" : " (último sinal de vida: " + quando + ")") + ".\n" +
                               (recentes.Count > 0
                                   ? "Há " + recentes.Count + " cópia(s) MAIS RECENTES do que o que estava guardado — escolhe a que queres repor."
                                   : "Não há cópias mais recentes do que o que estava guardado: o teu trabalho guardado está intacto.");
                JanelaRecuperacao.Abrir(aviso);
            };
        }

        static void Batimento(bool inicio)
        {
            try
            {
                string txt = File.Exists(FicheiroFlag) && !inicio ? File.ReadAllText(FicheiroFlag) : "";
                string inicioLinha = txt.Split('\n').FirstOrDefault(l => l.StartsWith("inicio=")) ?? ("inicio=" + Caminhos.Agora());
                File.WriteAllText(FicheiroFlag, inicioLinha + "\nultimo=" + Caminhos.Agora() + "\n");
            }
            catch (Exception) { }
        }

        static void AoSair()
        {
            try { if (File.Exists(FicheiroFlag)) File.Delete(FicheiroFlag); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ eventos
        static bool PodeAgora()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling
                   && !EditorApplication.isUpdating && !BuildPipeline.isBuildingPlayer;
        }

        static void Tick()
        {
            double agora = EditorApplication.timeSinceStartup;
            if (agora >= proximoBatimento) { proximoBatimento = agora + 60; Batimento(false); }

            var cfg = SegurancaConfig.I;
            if (!cfg.autoSaveAtivo || agora < proximo) return;
            if (!PodeAgora()) { proximo = agora + 10; return; }
            proximo = agora + Math.Max(0.5f, cfg.intervaloMinutos) * 60.0;
            FazerBackup("auto", true, false);
        }

        static void AoMudarPlay(PlayModeStateChange e)
        {
            if (e == PlayModeStateChange.ExitingEditMode && SegurancaConfig.I.autoSaveAtivo && SegurancaConfig.I.backupAntesDoPlay)
                FazerBackup("antes_play", true, false);
        }

        static void AntesDeRecompilar()
        {
            var cfg = SegurancaConfig.I;
            if (!cfg.autoSaveAtivo || !cfg.backupAntesDeCompilar || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if ((DateTime.Now - UltimoBackup).TotalSeconds < 60) return;
            FazerBackup("antes_compilar", true, false);
        }

        // ------------------------------------------------------------------ backup
        /// <returns>Número de cenas copiadas.</returns>
        public static int FazerBackup(string motivo, bool soComAlteracoes, bool avisar)
        {
            int n = 0;
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (!s.IsValid() || !s.isLoaded) continue;
                    if (soComAlteracoes && !s.isDirty) continue;
                    if (CopiarCena(s, motivo)) n++;
                }
                if (n > 0)
                {
                    UltimoBackup = DateTime.Now;
                    if (SegurancaConfig.I.guardarAssetsNoBackup) AssetDatabase.SaveAssets();
                }
            }
            catch (Exception e) { Debug.LogWarning(Caminhos.Log + "Backup falhou: " + e.Message); }

            if (avisar)
                EditorUtility.DisplayDialog("VirtuALLme · Backup",
                    n > 0 ? "Cópia de segurança feita de " + n + " cena(s)." : "Não há cenas abertas para copiar.", "OK");
            else if (n > 0)
                Debug.Log(Caminhos.Log + "Cópia de segurança (" + Motivo(motivo) + ") de " + n + " cena(s) às " + DateTime.Now.ToString("HH:mm:ss"));
            return n;
        }

        static string PastaDaCena(string nome, string caminho)
        {
            string id = string.IsNullOrEmpty(caminho) ? "semtitulo" : AssetDatabase.AssetPathToGUID(caminho);
            if (string.IsNullOrEmpty(id)) id = "x";
            if (id.Length > 8) id = id.Substring(0, 8);
            string pasta = Path.Combine(Caminhos.Backups, Caminhos.NomeSeguro(nome) + "_" + id);
            Caminhos.Garantir(pasta);
            return pasta;
        }

        static bool CopiarCena(Scene s, string motivo)
        {
            string nome = string.IsNullOrEmpty(s.path) ? "SemTitulo" : Path.GetFileNameWithoutExtension(s.path);
            string pasta = PastaDaCena(nome, s.path);
            string destino = Path.Combine(pasta, DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity");

            bool ok = false;
            AGuardarCopia = true;
            try
            {
                // 1ª tentativa: Temp/ (não é importado pelo Unity)
                if (!SessionState.GetBool(KTempFalha, false))
                {
                    string rel = "Temp/VirtuALLme_Autosave/" + Caminhos.NomeSeguro(nome) + ".unity";
                    Caminhos.Garantir(Path.GetDirectoryName(Caminhos.Abs(rel)));
                    string abs = Caminhos.Abs(rel);
                    if (File.Exists(abs)) File.Delete(abs);
                    try { ok = EditorSceneManager.SaveScene(s, rel, true); } catch (Exception) { ok = false; }
                    if (ok && File.Exists(abs)) { File.Copy(abs, destino, true); File.Delete(abs); }
                    else { ok = false; SessionState.SetBool(KTempFalha, true); }
                }
                // 2ª tentativa: ficheiro temporário em Assets/ (apagado logo a seguir)
                if (!ok)
                {
                    string rel = "Assets/__VirtuALLme_autosave_tmp.unity";
                    ok = EditorSceneManager.SaveScene(s, rel, true);
                    if (ok && File.Exists(Caminhos.Abs(rel))) File.Copy(Caminhos.Abs(rel), destino, true);
                    else ok = false;
                    AssetDatabase.DeleteAsset(rel);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(Caminhos.Log + "Não consegui copiar a cena \"" + nome + "\": " + e.Message);
                ok = false;
            }
            finally { AGuardarCopia = false; }

            if (!ok) return false;
            EscreverMeta(destino, s.path, nome, motivo);
            Limpar(pasta);
            return true;
        }

        static void EscreverMeta(string ficheiroUnity, string cenaOriginal, string nome, string motivo)
        {
            var meta = new MetaBackup
            {
                cenaOriginal = cenaOriginal, nome = nome, motivo = motivo,
                data = DateTime.Now.ToString("o"), unity = Application.unityVersion
            };
            File.WriteAllText(Path.ChangeExtension(ficheiroUnity, ".json"), JsonUtility.ToJson(meta, true));
        }

        static void Limpar(string pasta)
        {
            int max = Math.Max(3, SegurancaConfig.I.maxBackupsPorCena);
            var fs = Directory.GetFiles(pasta, "*.unity").OrderByDescending(f => Path.GetFileName(f)).ToList();
            foreach (var f in fs.Skip(max))
            {
                try
                {
                    File.Delete(f);
                    string j = Path.ChangeExtension(f, ".json");
                    if (File.Exists(j)) File.Delete(j);
                }
                catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------ listar / restaurar
        public static string Motivo(string m)
        {
            switch (m)
            {
                case "auto": return "Automático";
                case "antes_play": return "Antes do Play";
                case "antes_compilar": return "Antes de compilar";
                case "manual": return "Manual";
                case "antes_restaurar": return "Antes de restaurar";
                case "ficheiro_antes_restaurar": return "Ficheiro antes de restaurar";
                default: return m;
            }
        }

        public static List<InfoBackup> ListarBackups()
        {
            var l = new List<InfoBackup>();
            if (!Directory.Exists(Caminhos.Backups)) return l;
            foreach (var pasta in Directory.GetDirectories(Caminhos.Backups))
                foreach (var f in Directory.GetFiles(pasta, "*.unity"))
                {
                    var b = new InfoBackup { ficheiro = f, bytes = new FileInfo(f).Length, data = File.GetLastWriteTime(f) };
                    try
                    {
                        string j = Path.ChangeExtension(f, ".json");
                        if (File.Exists(j))
                        {
                            var m = JsonUtility.FromJson<MetaBackup>(File.ReadAllText(j));
                            b.cenaOriginal = m.cenaOriginal; b.nome = m.nome; b.motivo = m.motivo;
                            DateTime d;
                            if (DateTime.TryParse(m.data, null, System.Globalization.DateTimeStyles.RoundtripKind, out d)) b.data = d;
                        }
                    }
                    catch (Exception) { }
                    if (string.IsNullOrEmpty(b.nome)) b.nome = Path.GetFileName(pasta);
                    if (!string.IsNullOrEmpty(b.cenaOriginal))
                    {
                        string orig = Caminhos.Abs(b.cenaOriginal);
                        b.maisRecenteQueOriginal = !File.Exists(orig) || b.data > File.GetLastWriteTime(orig).AddSeconds(2);
                    }
                    else b.maisRecenteQueOriginal = true;
                    l.Add(b);
                }
            return l.OrderByDescending(b => b.data).ToList();
        }

        /// <param name="comoCopia">true = abre como nova cena em Assets/VirtuALLme_Recuperados (não toca no original)</param>
        public static void Restaurar(InfoBackup b, bool comoCopia)
        {
            if (b == null || !File.Exists(b.ficheiro)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("VirtuALLme", "Sai do Play Mode primeiro.", "OK");
                return;
            }

            ARestaurar = true;
            try
            {
                // 1) nada se perde: copiar o estado atual (cenas com alterações por guardar)
                FazerBackup("antes_restaurar", true, false);

                string alvo;
                if (comoCopia || string.IsNullOrEmpty(b.cenaOriginal))
                {
                    Caminhos.GarantirPastaAssets(Caminhos.PastaRecuperados + "/Cenas");
                    alvo = AssetDatabase.GenerateUniqueAssetPath(Caminhos.PastaRecuperados + "/Cenas/" +
                                                                 Caminhos.NomeSeguro(b.nome) + "_" + b.data.ToString("yyyyMMdd_HHmmss") + ".unity");
                }
                else
                {
                    alvo = b.cenaOriginal;
                    // 2) guardar também a versão que está no disco
                    string absAlvo = Caminhos.Abs(alvo);
                    if (File.Exists(absAlvo))
                    {
                        string pasta = PastaDaCena(Path.GetFileNameWithoutExtension(alvo), alvo);
                        string dest = Path.Combine(pasta, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_disco.unity");
                        File.Copy(absAlvo, dest, true);
                        EscreverMeta(dest, alvo, Path.GetFileNameWithoutExtension(alvo), "ficheiro_antes_restaurar");
                    }
                    Caminhos.Garantir(Path.GetDirectoryName(absAlvo));
                }

                // 3) fechar as cenas (já copiadas) para não haver conflito com o ficheiro
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                File.Copy(b.ficheiro, Caminhos.Abs(alvo), true);
                AssetDatabase.ImportAsset(alvo, ImportAssetOptions.ForceUpdate);
                EditorSceneManager.OpenScene(alvo, OpenSceneMode.Single);

                Debug.Log(Caminhos.Log + "Versão de " + b.data.ToString("dd/MM HH:mm:ss") + " reposta em " + alvo);
                EditorUtility.DisplayDialog("VirtuALLme · Recuperação",
                    "Versão de " + b.data.ToString("dd/MM/yyyy HH:mm:ss") + " reposta em:\n" + alvo +
                    (comoCopia ? "" : "\n\nO estado anterior ficou guardado nos backups (\"Antes de restaurar\").") +
                    "\n\nConfirma que está tudo certo e carrega Ctrl+S.", "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("VirtuALLme · Recuperação", "Não consegui restaurar: " + e.Message, "OK");
            }
            finally { ARestaurar = false; }
        }

        public static void AbrirPastaBackups()
        {
            Caminhos.Garantir(Caminhos.Backups);
            EditorUtility.RevealInFinder(Caminhos.Backups);
        }
    }
}
