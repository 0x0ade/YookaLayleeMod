﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Text;
using SGUI;
using Rewired;
using UEInput = UnityEngine.Input;
using System.IO;
using System.Reflection;
using System.Linq;
using Ionic.Zip;
using MonoMod.InlineRT;
using ReflectionHelper = MonoMod.InlineRT.ReflectionHelper;
using System.Security.Cryptography;
using MonoMod;
using Mono.Cecil;

namespace YLMAPI {
    public static class ModRelinker {

        public static string GameChecksum;

        public static Dictionary<string, ModuleDefinition> AssemblyRelinkedCache = new Dictionary<string, ModuleDefinition>() {
            { "Assembly-CSharp", ModuleDefinition.ReadModule(typeof(ModRelinker).Assembly.Location, new ReaderParameters(ReadingMode.Immediate)) },
            { "UnityEngine", ModuleDefinition.ReadModule(typeof(GameObject).Assembly.Location, new ReaderParameters(ReadingMode.Immediate)) }
        };

        private static Dictionary<string, ModuleDefinition> _AssemblyRelinkMap;
        public static Dictionary<string, ModuleDefinition> AssemblyRelinkMap {
            get {
                if (_AssemblyRelinkMap != null)
                    return _AssemblyRelinkMap;

                _AssemblyRelinkMap = new Dictionary<string, ModuleDefinition>();
                string[] entries = Directory.GetFiles(Path.GetDirectoryName(typeof(ModRelinker).Assembly.Location));
                for (int i = 0; i < entries.Length; i++) {
                    string path = entries[i];
                    string name = Path.GetFileName(path);
                    string nameNeutral = name.Substring(0, Math.Max(0, name.Length - 4));
                    if (name.EndsWith(".mm.dll")) {
                        if (name.StartsWith("Assembly-CSharp."))
                            _AssemblyRelinkMap[nameNeutral] = AssemblyRelinkedCache["Assembly-CSharp"];
                        else if (name.StartsWith("UnityEngine."))
                            _AssemblyRelinkMap[nameNeutral] = AssemblyRelinkedCache["UnityEngine"];
                        else {
                            ModLogger.Log("relinker", $"Found unknown {name}");
                            int dot = name.IndexOf('.');
                            if (dot < 0)
                                continue;
                            string nameRelinkedNeutral = name.Substring(0, dot);
                            string nameRelinked = nameRelinkedNeutral + ".dll";
                            string pathRelinked = Path.Combine(Path.GetDirectoryName(path), nameRelinked);
                            if (!File.Exists(pathRelinked))
                                continue;
                            ModuleDefinition relinked;
                            if (!AssemblyRelinkedCache.TryGetValue(nameRelinkedNeutral, out relinked)) {
                                relinked = ModuleDefinition.ReadModule(pathRelinked, new ReaderParameters(ReadingMode.Immediate));
                                AssemblyRelinkedCache[nameRelinkedNeutral] = relinked;
                            }
                            ModLogger.Log("relinker", $"Remapped to {nameRelinked}");
                            _AssemblyRelinkMap[nameNeutral] = relinked;
                        }
                    }
                }
                return _AssemblyRelinkMap;
            }
        }

        public static Assembly GetRelinkedAssembly(this GameModMetadata meta, Stream stream) {
            string cachedName = meta.Name + "." + meta.DLL.Substring(0, meta.DLL.Length - 3) + "dll";
            string cachedPath = Path.Combine(ModLoader.ModsCacheDirectory, cachedName);
            string cachedChecksumPath = Path.Combine(ModLoader.ModsCacheDirectory, cachedName + ".sum");

            string[] checksums = new string[2];
            using (MD5 md5 = MD5.Create()) {
                if (GameChecksum == null)
                    using (FileStream fs = File.OpenRead(Assembly.GetAssembly(typeof(ModRelinker)).Location))
                        GameChecksum = md5.ComputeHash(fs).ToHexadecimalString();
                checksums[0] = GameChecksum;

                string modPath = meta.Archive;
                if (modPath.Length == 0)
                    modPath = meta.DLL;
                using (FileStream fs = File.OpenRead(modPath))
                    checksums[1] = md5.ComputeHash(fs).ToHexadecimalString();
            }

            if (File.Exists(cachedPath) && File.Exists(cachedChecksumPath) &&
                checksums.ChecksumsEqual(File.ReadAllLines(cachedChecksumPath)))
                return Assembly.LoadFrom(cachedPath);

            using (MonoModder modder = new MonoModder() {
                Input = stream,
                OutputPath = cachedPath
            }) {
                modder.CleanupEnabled = false;

                modder.RelinkModuleMap = AssemblyRelinkMap;

                modder.ReaderParameters.ReadSymbols = false;
                modder.WriterParameters.WriteSymbols = false;
                modder.WriterParameters.SymbolWriterProvider = null;

                modder.Read();
                modder.MapDependencies();
                modder.AutoPatch();
                modder.Write();
            }

            return Assembly.LoadFrom(cachedPath);
        }


        public static string ToHexadecimalString(this byte[] data)
            => BitConverter.ToString(data).Replace("-", string.Empty);

        public static bool ChecksumsEqual(this string[] a, string[] b) {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].Trim() != b[i].Trim())
                    return false;
            return true;
        }

    }
}