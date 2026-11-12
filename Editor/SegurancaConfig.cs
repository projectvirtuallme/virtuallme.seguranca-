// VirtuALLme · Segurança do Projeto
// Configuração partilhada (fica em ProjectSettings/VirtuALLmeSeguranca.json e viaja com o projeto).

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VirtuALLme.Seguranca
{
    [Serializable]
    public class SegurancaConfig
    {
        // --- Auto-salvamento ---
        public bool autoSaveAtivo = true;
        public float intervaloMinutos = 3f;
        public int maxBackupsPorCena = 20;
        public bool guardarAssetsNoBackup = true;
        public bool backupAntesDoPlay = true;
        public bool backupAntesDeCompilar = true;
        public bool mostrarAvisoCrash = true;

        // --- Reparador ---
        public bool snapshotAutomatico = true;
        public bool avisarAoAbrirCena = true;
        public bool modoForte = false;            // repõe cores/texturas/materiais exatamente como no "estado bom"
        public bool restaurarGuidOriginal = true; // material recriado recebe o GUID antigo (repara referências no projeto inteiro)

        const string Ficheiro = "ProjectSettings/VirtuALLmeSeguranca.json";
        static SegurancaConfig _i;

        public static SegurancaConfig I
        {
            get { if (_i == null) Carregar(); return _i; }
        }

        public static void Carregar()
        {
            try
            {
                string p = Caminhos.Abs(Ficheiro);
                if (File.Exists(p)) _i = JsonUtility.FromJson<SegurancaConfig>(File.ReadAllText(p));
            }
            catch (Exception) { _i = null; }
            if (_i == null) _i = new SegurancaConfig();
        }

        public void Guardar()
        {
            try { File.WriteAllText(Caminhos.Abs(Ficheiro), JsonUtility.ToJson(this, true)); }
            catch (Exception e) { Debug.LogWarning(Caminhos.Log + "Não consegui gravar a configuração: " + e.Message); }
        }
    }

    internal static class Caminhos
    {
        public const string Log = "[VirtuALLme Segurança] ";
        public const string PastaRecuperados = "Assets/VirtuALLme_Recuperados";

        public static string RaizProjeto { get { return Path.GetDirectoryName(Application.dataPath); } }
        public static string Pasta { get { return Path.Combine(RaizProjeto, "VirtuALLme_Seguranca"); } }
        public static string Snapshots { get { return Path.Combine(Pasta, "Snapshots"); } }
        public static string Backups { get { return Path.Combine(Pasta, "Backups"); } }

        public static string Abs(string relativo)
        {
            if (Path.IsPathRooted(relativo)) return relativo;
            return Path.GetFullPath(Path.Combine(RaizProjeto, relativo));
        }

        public static void Garantir(string pastaAbs)
        {
            if (!Directory.Exists(pastaAbs)) Directory.CreateDirectory(pastaAbs);
        }

        /// <summary>Cria (se preciso) uma pasta dentro de Assets através do AssetDatabase.</summary>
        public static void GarantirPastaAssets(string pastaAssets)
        {
            pastaAssets = pastaAssets.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(pastaAssets)) return;
            string pai = Path.GetDirectoryName(pastaAssets).Replace('\\', '/');
            GarantirPastaAssets(pai);
            AssetDatabase.CreateFolder(pai, Path.GetFileName(pastaAssets));
        }

        public static string Agora() { return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); }

        public static string NomeSeguro(string s)
        {
            if (string.IsNullOrEmpty(s)) return "SemNome";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
