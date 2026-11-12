// VirtuALLme · Segurança do Projeto
// Menus, Painel de Segurança (janela acoplável com os botões grandes),
// janela "Recuperar versão" e um pequeno painel flutuante na Scene View.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VirtuALLme.Seguranca
{
    static class MenusSeguranca
    {
        const string Raiz = "VirtuALLme/Segurança/";

        [MenuItem(Raiz + "Painel de Segurança", false, 0)]
        static void Painel() { PainelSeguranca.Abrir(); }

        [MenuItem(Raiz + "Reparar cena agora %&r", false, 20)]
        static void Reparar() { ReparadorDeCena.Reparar(true); }

        [MenuItem(Raiz + "Guardar estado bom da cena %&g", false, 21)]
        static void EstadoBom() { ReparadorDeCena.GuardarEstadoBom(); }

        [MenuItem(Raiz + "Remover scripts em falta", false, 22)]
        static void Scripts() { ReparadorDeCena.RemoverScriptsEmFalta(); }

        [MenuItem(Raiz + "Backup agora %&b", false, 40)]
        static void Backup() { AutoSalvamento.FazerBackup("manual", false, true); }

        [MenuItem(Raiz + "Recuperar versão anterior...", false, 41)]
        static void Recuperar() { JanelaRecuperacao.Abrir(null); }

        [MenuItem(Raiz + "Abrir pasta de backups", false, 42)]
        static void Pasta() { AutoSalvamento.AbrirPastaBackups(); }
    }

    static class Estilos
    {
        static GUIStyle _titulo, _botao, _sub;
        public static GUIStyle Titulo
        {
            get
            {
                if (_titulo == null) _titulo = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
                return _titulo;
            }
        }
        public static GUIStyle Sub
        {
            get
            {
                if (_sub == null) _sub = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
                return _sub;
            }
        }
        public static GUIStyle Botao
        {
            get
            {
                if (_botao == null) _botao = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold, fixedHeight = 38 };
                return _botao;
            }
        }
        public static readonly Color Verde = new Color(0.45f, 0.85f, 0.55f);
        public static readonly Color Azul = new Color(0.5f, 0.75f, 1f);
        public static readonly Color Laranja = new Color(1f, 0.75f, 0.4f);

        public static bool BotaoGrande(string texto, Color cor)
        {
            var antes = GUI.backgroundColor;
            GUI.backgroundColor = cor;
            bool r = GUILayout.Button(texto, Botao);
            GUI.backgroundColor = antes;
            return r;
        }
    }

    public class PainelSeguranca : EditorWindow
    {
        Vector2 scroll;
        string infoEstadoBom = "";
        double proximaAtualizacao;

        public static void Abrir()
        {
            var w = GetWindow<PainelSeguranca>("VirtuALLme Segurança");
            w.minSize = new Vector2(330, 460);
            w.Show();
        }

        void OnFocus() { Atualizar(); }
        void OnInspectorUpdate()
        {
            if (EditorApplication.timeSinceStartup > proximaAtualizacao) { Atualizar(); Repaint(); }
        }

        void Atualizar()
        {
            proximaAtualizacao = EditorApplication.timeSinceStartup + 3;
            var s = SceneManager.GetActiveScene();
            var snap = ReparadorDeCena.Ler(s);
            if (string.IsNullOrEmpty(s.path)) infoEstadoBom = "Cena sem nome — guarda-a primeiro (Ctrl+S).";
            else if (snap == null) infoEstadoBom = "Ainda sem registo desta cena (é criado ao guardar/abrir).";
            else infoEstadoBom = "Último estado bom: " + (string.IsNullOrEmpty(snap.estadoBomEm) ? "nunca" : snap.estadoBomEm) +
                                 "\nÚltimo registo automático: " + snap.atualizado;
        }

        void OnGUI()
        {
            var cfg = SegurancaConfig.I;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(6);
            GUILayout.Label("VirtuALLme · Segurança do Projeto", Estilos.Titulo);
            EditorGUILayout.Space(6);

            // ---------------------------------------------------------------- 1
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("1 · Reparar a cena", Estilos.Sub);
            EditorGUILayout.LabelField("Repõe materiais, cores, texturas, shaders rosa, meshes e prefabs perdidos.", EditorStyles.wordWrappedMiniLabel);
            if (Estilos.BotaoGrande("Reparar cena agora   (Ctrl+Alt+R)", Estilos.Verde))
                EditorApplication.delayCall += () => { ReparadorDeCena.Reparar(true); Atualizar(); };
            if (Estilos.BotaoGrande("Guardar estado bom   (a cena está certinha)", Estilos.Azul))
                EditorApplication.delayCall += () => { ReparadorDeCena.GuardarEstadoBom(); Atualizar(); };
            EditorGUILayout.LabelField(infoEstadoBom, EditorStyles.wordWrappedMiniLabel);

            EditorGUI.BeginChangeCheck();
            cfg.modoForte = EditorGUILayout.ToggleLeft(new GUIContent("Modo forte: repor cores/materiais EXATAMENTE como no estado bom",
                "Desligado: só preenche o que está vazio, partido ou rosa.\nLigado: também desfaz alterações de cor/textura/material feitas depois do estado bom."), cfg.modoForte);
            cfg.avisarAoAbrirCena = EditorGUILayout.ToggleLeft("Avisar quando uma cena abre com problemas", cfg.avisarAoAbrirCena);
            cfg.snapshotAutomatico = EditorGUILayout.ToggleLeft("Registar a cena automaticamente ao guardar/abrir", cfg.snapshotAutomatico);
            cfg.restaurarGuidOriginal = EditorGUILayout.ToggleLeft(new GUIContent("Material recriado recebe o GUID original",
                "Faz com que prefabs e outras cenas que usavam o material apagado voltem a encontrá-lo."), cfg.restaurarGuidOriginal);
            if (GUILayout.Button("Remover componentes com script em falta"))
                EditorApplication.delayCall += ReparadorDeCena.RemoverScriptsEmFalta;
            if (!string.IsNullOrEmpty(ReparadorDeCena.UltimoRelatorio))
                EditorGUILayout.HelpBox("Última reparação: " + ReparadorDeCena.UltimoRelatorio, MessageType.None);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // ---------------------------------------------------------------- 2
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("2 · Auto-salvamento e recuperação", Estilos.Sub);
            EditorGUILayout.LabelField("Cópias automáticas da cena para recuperar a versão exata depois de um crash, travamento ou reinício.", EditorStyles.wordWrappedMiniLabel);
            if (Estilos.BotaoGrande("Recuperar versão anterior…", Estilos.Laranja))
                EditorApplication.delayCall += () => JanelaRecuperacao.Abrir(null);
            if (GUILayout.Button("Fazer cópia agora   (Ctrl+Alt+B)", GUILayout.Height(26)))
                EditorApplication.delayCall += () => AutoSalvamento.FazerBackup("manual", false, true);

            var ub = AutoSalvamento.UltimoBackup;
            EditorGUILayout.LabelField("Última cópia nesta sessão: " + (ub == DateTime.MinValue ? "—" : ub.ToString("HH:mm:ss")), EditorStyles.miniLabel);

            cfg.autoSaveAtivo = EditorGUILayout.ToggleLeft("Auto-salvamento ligado", cfg.autoSaveAtivo);
            using (new EditorGUI.DisabledScope(!cfg.autoSaveAtivo))
            {
                cfg.intervaloMinutos = EditorGUILayout.Slider("Intervalo (minutos)", cfg.intervaloMinutos, 1f, 30f);
                cfg.maxBackupsPorCena = EditorGUILayout.IntSlider("Cópias guardadas por cena", cfg.maxBackupsPorCena, 3, 100);
                cfg.backupAntesDoPlay = EditorGUILayout.ToggleLeft("Copiar antes de entrar em Play", cfg.backupAntesDoPlay);
                cfg.backupAntesDeCompilar = EditorGUILayout.ToggleLeft("Copiar antes de recompilar scripts", cfg.backupAntesDeCompilar);
                cfg.guardarAssetsNoBackup = EditorGUILayout.ToggleLeft(new GUIContent("Gravar também materiais/assets alterados",
                    "Executa 'Save Project' em cada cópia, para que alterações em materiais, prefabs e ScriptableObjects não se percam num crash."), cfg.guardarAssetsNoBackup);
            }
            cfg.mostrarAvisoCrash = EditorGUILayout.ToggleLeft("Abrir a recuperação automaticamente após um crash", cfg.mostrarAvisoCrash);
            if (GUILayout.Button("Abrir pasta de backups")) AutoSalvamento.AbrirPastaBackups();
            if (EditorGUI.EndChangeCheck()) cfg.Guardar();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Os registos ficam na pasta VirtuALLme_Seguranca/ (raiz do projeto).", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();
        }
    }

    public class JanelaRecuperacao : EditorWindow
    {
        List<InfoBackup> lista;
        string aviso;
        Vector2 scroll;
        bool soRecentes;
        readonly Dictionary<string, bool> abertos = new Dictionary<string, bool>();

        public static void Abrir(string avisoCrash)
        {
            var w = GetWindow<JanelaRecuperacao>(true, "VirtuALLme · Recuperar versão", true);
            w.aviso = avisoCrash;
            w.soRecentes = !string.IsNullOrEmpty(avisoCrash);
            w.minSize = new Vector2(620, 400);
            w.Recarregar();
            w.Show();
        }

        void Recarregar() { lista = AutoSalvamento.ListarBackups(); }

        static string Tamanho(long b)
        {
            if (b > 1024 * 1024) return (b / (1024f * 1024f)).ToString("0.0") + " MB";
            return (b / 1024f).ToString("0") + " KB";
        }

        void OnGUI()
        {
            if (lista == null) Recarregar();
            EditorGUILayout.Space(4);
            GUILayout.Label("Recuperar uma versão anterior da cena", Estilos.Titulo);
            if (!string.IsNullOrEmpty(aviso)) EditorGUILayout.HelpBox(aviso, MessageType.Warning);
            EditorGUILayout.HelpBox("\"Restaurar\" repõe a cena original nessa versão (o estado atual é copiado antes, por segurança).\n" +
                                    "\"Abrir cópia\" abre essa versão como cena nova em Assets/VirtuALLme_Recuperados, sem mexer no original.\n" +
                                    "★ = mais recente do que o ficheiro guardado no disco (trabalho que não chegou a ser guardado).", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            soRecentes = EditorGUILayout.ToggleLeft("Mostrar só as mais recentes do que o guardado (★)", soRecentes);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Atualizar", GUILayout.Width(90))) Recarregar();
            if (GUILayout.Button("Abrir pasta", GUILayout.Width(90))) AutoSalvamento.AbrirPastaBackups();
            EditorGUILayout.EndHorizontal();

            var visiveis = soRecentes ? lista.Where(b => b.maisRecenteQueOriginal).ToList() : lista;
            if (visiveis.Count == 0)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.LabelField(soRecentes ? "Não há cópias mais recentes do que o que está guardado." : "Ainda não há cópias de segurança.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var grupo in visiveis.GroupBy(b => string.IsNullOrEmpty(b.cenaOriginal) ? "(cena sem nome)" : b.cenaOriginal))
            {
                bool aberto;
                if (!abertos.TryGetValue(grupo.Key, out aberto)) aberto = true;
                aberto = EditorGUILayout.Foldout(aberto, grupo.Key + "   (" + grupo.Count() + ")", true, EditorStyles.foldoutHeader);
                abertos[grupo.Key] = aberto;
                if (!aberto) continue;

                foreach (var b in grupo.Take(40))
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    GUILayout.Label(b.maisRecenteQueOriginal ? "★" : " ", GUILayout.Width(14));
                    GUILayout.Label(b.data.ToString("dd/MM/yyyy  HH:mm:ss"), EditorStyles.boldLabel, GUILayout.Width(150));
                    GUILayout.Label(AutoSalvamento.Motivo(b.motivo), GUILayout.Width(150));
                    GUILayout.Label(Tamanho(b.bytes), EditorStyles.miniLabel, GUILayout.Width(60));
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(b.cenaOriginal)))
                    {
                        var antes = GUI.backgroundColor;
                        GUI.backgroundColor = Estilos.Laranja;
                        if (GUILayout.Button("Restaurar", GUILayout.Width(85)))
                        {
                            var alvo = b;
                            EditorApplication.delayCall += () =>
                            {
                                if (EditorUtility.DisplayDialog("Restaurar versão",
                                        "Repor \"" + alvo.cenaOriginal + "\" na versão de " + alvo.data.ToString("dd/MM HH:mm:ss") + "?\n\n" +
                                        "O estado atual é copiado antes (não se perde nada).", "Restaurar", "Cancelar"))
                                {
                                    AutoSalvamento.Restaurar(alvo, false);
                                    Recarregar();
                                }
                            };
                        }
                        GUI.backgroundColor = antes;
                    }
                    if (GUILayout.Button("Abrir cópia", GUILayout.Width(85)))
                    {
                        var alvo = b;
                        EditorApplication.delayCall += () => { AutoSalvamento.Restaurar(alvo, true); Recarregar(); };
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }

#if UNITY_2021_2_OR_NEWER
    [UnityEditor.Overlays.Overlay(typeof(SceneView), "virtuallme-seguranca", "VirtuALLme Segurança", true)]
    class OverlaySeguranca : UnityEditor.Overlays.IMGUIOverlay
    {
        public override void OnGUI()
        {
            if (GUILayout.Button("Reparar cena", GUILayout.MinWidth(120)))
                EditorApplication.delayCall += () => ReparadorDeCena.Reparar(true);
            if (GUILayout.Button("Guardar estado bom"))
                EditorApplication.delayCall += ReparadorDeCena.GuardarEstadoBom;
            if (GUILayout.Button("Recuperar versão"))
                EditorApplication.delayCall += () => JanelaRecuperacao.Abrir(null);
            if (GUILayout.Button("Painel…"))
                EditorApplication.delayCall += PainelSeguranca.Abrir;
        }
    }
#endif
}
