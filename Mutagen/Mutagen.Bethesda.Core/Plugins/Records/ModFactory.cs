using Noggog;
using System.Linq.Expressions;
using System.Reflection;
using DynamicData;
using Loqui;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Records.Loqui;

namespace Mutagen.Bethesda.Plugins.Records
{






    public static class ModFactory<TMod>
        where TMod : IModGetter
    {
        public delegate TMod ActivatorDelegate(ModKey modKey, GameRelease release, float? headerVersion = null, bool? forceUseLowerFormIDRanges = false);
        public delegate TMod ImporterDelegate(ModPath modKey, GameRelease release, BinaryReadParameters? param = null);
        public delegate TMod ImportMultiFileGetterDelegate(ModKey targetModKey, IEnumerable<ModPath> splitFiles, IEnumerable<ModKey> loadOrder, GameRelease release, BinaryReadParameters? param = null);
        public delegate TMod ImportGetterWithMultiFileDetectionDelegate(ModPath modPath, IEnumerable<ModKey> loadOrder, GameRelease release, BinaryReadParameters? param = null);
        public delegate TMod ImportSetterWithMultiFileDetectionDelegate(ModPath modPath, IEnumerable<ModKey> loadOrder, GameRelease release, BinaryReadParameters? param = null);




        public static readonly ActivatorDelegate Activator;




        public static readonly ImporterDelegate Importer;




        public static readonly ImportMultiFileGetterDelegate ImportMultiFileGetter;





        public static readonly ImportGetterWithMultiFileDetectionDelegate ImportGetterWithMultiFileDetection;





        public static readonly ImportSetterWithMultiFileDetectionDelegate ImportSetterWithMultiFileDetection;

        static ModFactory()
        {
            Warmup.Init();
            bool createActivator = true;
            var type = typeof(TMod);
            if (type.Name.EndsWith("DisposableGetter"))
            {
                var className = type.Name.TrimStringFromEnd("DisposableGetter") + "Getter";
                type = Type.GetType($"{type.Namespace}.{className}, {type.Namespace}");
                createActivator = false;
            }

            if (type == null)
            {
                throw new ArgumentException();
            }

            if (type == typeof(IModGetter) || type == typeof(IMod))
            {
                Activator = (modKey, release, headerVersion, forceUseLowerFormIDRanges) => (TMod)ModFactory.Activator(modKey, release, headerVersion, forceUseLowerFormIDRanges);
                if (type == typeof(IModGetter))
                {
                    Importer = (path, release, param) => (TMod)ModFactory.ImportGetter(path, release, param);
                    ImportMultiFileGetter = (targetModKey, splitFiles, loadOrder, release, param) =>
                        (TMod)ModFactory.ImportMultiFileGetter(targetModKey, splitFiles, loadOrder, release, param);
                    ImportGetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        (TMod)ModFactory.ImportGetterWithMultiFileDetection(modPath, loadOrder, release, param);
                    ImportSetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        throw new InvalidOperationException("ImportSetterWithMultiFileDetection is only supported for setter types (IMod), not getter types (IModGetter)");
                }
                else
                {
                    Importer = (path, release, param) => (TMod)ModFactory.ImportSetter(path, release, param);
                    ImportMultiFileGetter = (targetModKey, splitFiles, loadOrder, release, param) =>
                        throw new InvalidOperationException("ImportMultiFileGetter is only supported for getter types (IModGetter), not setter types (IMod)");
                    ImportGetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        throw new InvalidOperationException("ImportGetterWithMultiFileDetection is only supported for getter types (IModGetter), not setter types (IMod)");
                    ImportSetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        (TMod)ModFactory.ImportSetterWithMultiFileDetection(modPath, loadOrder, release, param);
                }
            }
            else
            {
                if (!LoquiRegistration.TryGetRegister(type, out var regis))
                {
                    throw new ArgumentException();
                }

                if (createActivator)
                {
                    Activator = ModFactoryReflection.GetActivator<TMod>(regis);
                }
                else
                {
                    Activator = (Key, Release, Version, Ranges) =>
                    {
                        throw new ArgumentException($"Cannot create a new mod of type {type}");
                    };
                }
                if (typeof(TMod).InheritsFrom(typeof(IMod)))
                {
                    Importer = ModFactoryReflection.GetImporter<TMod>(regis);
                    ImportMultiFileGetter = (targetModKey, splitFiles, loadOrder, release, param) =>
                        throw new InvalidOperationException("ImportMultiFileGetter is only supported for getter/overlay types, not mutable mod types");
                    ImportGetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        throw new InvalidOperationException("ImportGetterWithMultiFileDetection is only supported for getter/overlay types, not mutable mod types");
                    ImportSetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        (TMod)ModFactory.ImportSetterWithMultiFileDetection(modPath, loadOrder, release, param);
                }
                else
                {
                    Importer = ModFactoryReflection.GetOverlay<TMod>(regis);
                    ImportMultiFileGetter = (targetModKey, splitFiles, loadOrder, release, param) =>
                        (TMod)ModFactory.ImportMultiFileGetter(targetModKey, splitFiles, loadOrder, release, param);
                    ImportGetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        (TMod)ModFactory.ImportGetterWithMultiFileDetection(modPath, loadOrder, release, param);
                    ImportSetterWithMultiFileDetection = (modPath, loadOrder, release, param) =>
                        throw new InvalidOperationException("ImportSetterWithMultiFileDetection is only supported for mutable mod types, not getter/overlay types");
                }
            }
        }
    }




    public static class ModFactory
    {
        record Delegates(
            ModFactory<IModDisposeGetter>.ImporterDelegate ImportGetter,
            ModFactory<IMod>.ImporterDelegate ImportSetter,
            ModFactory<IMod>.ActivatorDelegate Activator);

        private static Dictionary<GameCategory, Delegates> _dict = new();

        static ModFactory()
        {
            foreach (var category in Enums<GameCategory>.Values)
            {
                var t = Type.GetType(
                    $"Mutagen.Bethesda.{category}.{category}Mod_Registration, Mutagen.Bethesda.{category}");
                if (t == null) continue;
                var obj = System.Activator.CreateInstance(t);
                var modRegistration = obj as IModRegistration;
                if (modRegistration == null) continue;
                _dict[modRegistration.GameCategory] = new Delegates(
                    ModFactoryReflection.GetOverlay<IModDisposeGetter>(modRegistration),
                    ModFactoryReflection.GetImporter<IMod>(modRegistration),
                    ModFactoryReflection.GetActivator<IMod>(modRegistration));

            }
        }

        public static IModDisposeGetter ImportGetter(ModPath path, GameRelease release, BinaryReadParameters? param = null)
        {
            return _dict[release.ToCategory()].ImportGetter(path, release, param);
        }

        public static IMod ImportSetter(ModPath path, GameRelease release, BinaryReadParameters? param = null)
        {
            return _dict[release.ToCategory()].ImportSetter(path, release, param);
        }

        public static IMod Activator(ModKey modKey, GameRelease release, float? headerVersion = null, bool? forceUseLowerFormIDRanges = false)
        {
            return _dict[release.ToCategory()].Activator(modKey, release, headerVersion: headerVersion, forceUseLowerFormIDRanges: forceUseLowerFormIDRanges);
        }










        public static IModDisposeGetter ImportGetterWithMultiFileDetection(
            ModPath modPath,
            IEnumerable<ModKey> loadOrder,
            GameRelease release,
            BinaryReadParameters? param = null)
        {
            var fileSystem = param?.FileSystem ?? new System.IO.Abstractions.FileSystem();


            if (Analysis.MultiModFileAnalysis.IsMultiModFile(modPath, fileSystem))
            {

                var splitFiles = Analysis.MultiModFileAnalysis.GetSplitModFiles(modPath, fileSystem);


                return ImportMultiFileGetter(
                    modPath.ModKey,
                    splitFiles.Select(f => (ModPath)f.Path),
                    loadOrder,
                    release,
                    param);
            }
            else
            {

                return ImportGetter(modPath, release, param);
            }
        }










        public static IMod ImportSetterWithMultiFileDetection(
            ModPath modPath,
            IEnumerable<ModKey> loadOrder,
            GameRelease release,
            BinaryReadParameters? param = null)
        {

            using var getter = ImportGetterWithMultiFileDetection(modPath, loadOrder, release, param);


            return getter.DeepCopy();
        }










        public static IModDisposeGetter ImportMultiFileGetter(
            ModKey targetModKey,
            IEnumerable<ModPath> splitFiles,
            IEnumerable<ModKey> loadOrder,
            GameRelease release,
            BinaryReadParameters? param = null)
        {
            param ??= BinaryReadParameters.Default;



            var splitModKeys = new HashSet<ModKey> { targetModKey };
            var splitFilesList = new List<ModPath>();
            foreach (var splitFile in splitFiles)
            {
                var actualModKey = ModKey.FromFileName(Path.GetFileName(splitFile.Path));
                splitModKeys.Add(actualModKey);
                splitFilesList.Add(new ModPath(targetModKey, splitFile.Path));
            }


            var overlays = new List<IModDisposeGetter>();
            foreach (var splitFile in splitFilesList)
            {

                var header = ModHeaderFrame.FromPath(splitFile, release, fileSystem: param.FileSystem);


                var remappedMasters = header.Masters(splitFile.ModKey)
                    .Select(m => splitModKeys.Contains(m.Master)
                        ? (IMasterReferenceGetter)new MasterReference { Master = targetModKey }
                        : m)
                    .ToList();

                var splitParam = param with
                {
                    MasterOverrides = MasterReferenceCollection.CreateUnsafe(targetModKey, remappedMasters)
                };

                var overlay = ImportGetter(splitFile, release, splitParam);
                overlays.Add(overlay);
            }


            ValidateNoDuplicates(overlays, targetModKey, release);




            var mergedMasters = MergeMasters(overlays, loadOrder, splitModKeys);


            return CreateMultiFileOverlay(targetModKey, release, overlays, mergedMasters);
        }

        private static IReadOnlyList<IMasterReferenceGetter> MergeMasters(
            List<IModDisposeGetter> overlays,
            IEnumerable<ModKey> loadOrder,
            HashSet<ModKey> excludedModKeys)
        {



            var allMasters = new HashSet<ModKey>();
            foreach (var overlay in overlays)
            {
                foreach (var master in overlay.MasterReferences)
                {
                    if (!excludedModKeys.Contains(master.Master))
                    {
                        allMasters.Add(master.Master);
                    }
                }
            }


            var loadOrderList = loadOrder.ToList();
            var loadOrderDict = loadOrderList
                .Select((m, i) => new { ModKey = m, Index = i })
                .ToDictionary(x => x.ModKey, x => x.Index);


            var orderedMasterKeys = allMasters
                .OrderBy(m => loadOrderDict.TryGetValue(m, out var index) ? index : int.MaxValue)
                .ThenBy(m => m.FileName.String)
                .ToList();


            var result = new List<IMasterReferenceGetter>();
            foreach (var masterKey in orderedMasterKeys)
            {
                result.Add(new MasterReference { Master = masterKey });
            }

            return result.AsReadOnly();
        }

        private static IModDisposeGetter CreateMultiFileOverlay(
            ModKey modKey,
            GameRelease gameRelease,
            List<IModDisposeGetter> overlays,
            IReadOnlyList<IMasterReferenceGetter> mergedMasters)
        {

            var (typeName, assemblyName) = gameRelease.ToCategory().GetMultiFileOverlayTypeInfo();


            var assemblyQualifiedName = $"{typeName}, {assemblyName}";
            var overlayType = Type.GetType(assemblyQualifiedName);
            if (overlayType == null)
            {
                throw new InvalidOperationException($"Could not find multi-file overlay type: {assemblyQualifiedName}");
            }



            var constructor = overlayType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(c =>
                {
                    var parameters = c.GetParameters();
                    if (parameters.Length != 3) return false;
                    if (parameters[0].ParameterType != typeof(ModKey)) return false;

                    if (!parameters[1].ParameterType.IsGenericType) return false;
                    var genDef = parameters[1].ParameterType.GetGenericTypeDefinition();
                    if (genDef != typeof(IEnumerable<>)) return false;
                    var listElementType = parameters[1].ParameterType.GetGenericArguments()[0];
                    if (!typeof(IModGetter).IsAssignableFrom(listElementType)) return false;
                    if (parameters[2].ParameterType != typeof(IReadOnlyList<IMasterReferenceGetter>)) return false;
                    return true;
                });

            if (constructor == null)
            {
                throw new InvalidOperationException($"Could not find appropriate constructor for {assemblyQualifiedName}");
            }


            var listParameterType = constructor.GetParameters()[1].ParameterType;
            var listElementType = listParameterType.GetGenericArguments()[0];



            var typedListType = typeof(List<>).MakeGenericType(listElementType);
            var typedList = (System.Collections.IList)System.Activator.CreateInstance(typedListType)!;
            foreach (var item in overlays)
            {
                typedList.Add(item);
            }


            var overlay = constructor.Invoke(new object[]
            {
                modKey,
                typedList,
                mergedMasters
            });

            if (overlay == null)
            {
                throw new InvalidOperationException($"Failed to create multi-file overlay of type {assemblyQualifiedName}");
            }

            return (IModDisposeGetter)overlay;
        }

        private static void ValidateNoDuplicates(List<IModDisposeGetter> overlays, ModKey modKey, GameRelease release)
        {
            var parentRecordTypes = Meta.GameConstants.Get(release).GroupConstants.ParentRecordTypes;
            var seenFormKeys = new Dictionary<FormKey, string>();

            for (int i = 0; i < overlays.Count; i++)
            {
                var overlay = overlays[i];
                var fileName = $"{modKey.FileName.String.Replace(modKey.Type.ToString(), "")}_{i + 1}.{modKey.Type}";

                foreach (var record in overlay.EnumerateMajorRecords())
                {
                    if (seenFormKeys.TryGetValue(record.FormKey, out var previousFile))
                    {
                        if (parentRecordTypes.Contains(Mapping.RecordTypeLookup.GetRecordType(record.GetType())))
                        {




                            seenFormKeys[record.FormKey] = fileName;
                            continue;
                        }

                        throw new InvalidOperationException(
                            $"Duplicate FormKey {record.FormKey} found in both {previousFile} and {fileName}. " +
                            "This indicates corruption in the split files.");
                    }

                    seenFormKeys[record.FormKey] = fileName;
                }
            }
        }
    }

    internal static class ModFactoryReflection
    {
        internal static ModFactory<TMod>.ActivatorDelegate GetActivator<TMod>(ILoquiRegistration regis)
            where TMod : IModGetter
        {
            var ctorInfo = regis.ClassType.GetConstructors()
                .Where(c => c.GetParameters().Length >= 3)
                .Where(c => c.GetParameters()[0].ParameterType == typeof(ModKey))
                .First();
            var paramInfo = ctorInfo.GetParameters();
            ParameterExpression modKeyParam = Expression.Parameter(typeof(ModKey), "modKey");
            ParameterExpression headerVersionParam = Expression.Parameter(typeof(float?), "headerVersion");
            ParameterExpression forceUseLowerFormIDRangesParam = Expression.Parameter(typeof(bool?), "forceUseLowerFormIDRanges");
            if (paramInfo.Length == 3)
            {
                NewExpression newExp = Expression.New(ctorInfo, modKeyParam, headerVersionParam, forceUseLowerFormIDRangesParam);
                LambdaExpression lambda = Expression.Lambda(typeof(Func<ModKey, float?, bool?, TMod>), newExp, modKeyParam, headerVersionParam, forceUseLowerFormIDRangesParam);
                var deleg = lambda.Compile();
                return (ModKey modKey, GameRelease release, float? headerVersion = null, bool? forceUseLowerFormIDRanges = false) =>
                {
                    return (TMod)deleg.DynamicInvoke(modKey, headerVersion, forceUseLowerFormIDRanges)!;
                };
            }
            else
            {
                ParameterExpression releaseParam = Expression.Parameter(paramInfo[1].ParameterType, "release");
                NewExpression newExp = Expression.New(ctorInfo, modKeyParam, releaseParam, headerVersionParam, forceUseLowerFormIDRangesParam);
                var funcType = Expression.GetFuncType(typeof(ModKey), paramInfo[1].ParameterType, typeof(float?), typeof(bool?), typeof(TMod));
                LambdaExpression lambda = Expression.Lambda(funcType, newExp, modKeyParam, releaseParam, headerVersionParam, forceUseLowerFormIDRangesParam);
                var deleg = lambda.Compile();
                return (ModKey modKey, GameRelease release, float? headerVersion = null, bool? forceUseLowerFormIDRanges = false) =>
                {
                    return (TMod)deleg.DynamicInvoke(modKey, (int)release, headerVersion, forceUseLowerFormIDRanges)!;
                };
            }
        }

        public static ModFactory<TMod>.ImporterDelegate GetImporter<TMod>(ILoquiRegistration regis)
            where TMod : IModGetter
        {
            var methodInfo = regis.ClassType.GetMethods()
                .Where(m => m.Name == "CreateFromBinary")
                .Where(c => c.GetParameters().Length >= 3)
                .Where(c => c.GetParameters()[0].ParameterType == typeof(ModPath))
                .First();
            var paramInfo = methodInfo.GetParameters();
            var paramExprs = paramInfo.Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            MethodCallExpression callExp = Expression.Call(methodInfo, paramExprs);
            var funcType =
                Expression.GetFuncType(paramInfo.Select(p => p.ParameterType).And(typeof(TMod)).ToArray());
            LambdaExpression lambda = Expression.Lambda(funcType, callExp, paramExprs);
            var deleg = lambda.Compile();
            var releaseIndex = paramInfo.Select(x => x.Name).IndexOf("release");
            var fileSystemIndex = paramInfo.Select(x => x.Name).IndexOf("fileSystem");
            var paramIndex = paramInfo.Select(x => x.Name).IndexOf("param");
            return (ModPath modPath, GameRelease release, BinaryReadParameters? param) =>
            {
                var args = new object?[paramInfo.Length];
                args[0] = modPath;
                if (releaseIndex != -1)
                {
                    args[releaseIndex] = release;
                }

                if (paramIndex != -1)
                {
                    args[paramIndex] = param;
                }

                return (TMod)deleg.DynamicInvoke(args)!;
            };
        }

        public static ModFactory<TMod>.ImporterDelegate GetOverlay<TMod>(ILoquiRegistration regis)
            where TMod : IModGetter
        {
            var methodInfo = regis.ClassType.GetMethods()
                .Where(m => m.Name == "CreateFromBinaryOverlay")
                .Where(c => c.GetParameters().Length >= 1)
                .Where(c => c.GetParameters()[0].ParameterType == typeof(ModPath))
                .First();
            var paramInfo = methodInfo.GetParameters();
            var paramExprs = paramInfo.Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            MethodCallExpression callExp = Expression.Call(methodInfo, paramExprs);
            var funcType =
                Expression.GetFuncType(paramInfo.Select(p => p.ParameterType).And(regis.GetterType).ToArray());
            LambdaExpression lambda = Expression.Lambda(funcType, callExp, paramExprs);
            var deleg = lambda.Compile();
            var releaseIndex = paramInfo.Select(x => x.Name).IndexOf("release");
            var paramIndex = paramInfo.Select(x => x.Name).IndexOf("param");
            return (ModPath modPath, GameRelease release, BinaryReadParameters? param) =>
            {
                var args = new object?[paramInfo.Length];
                args[0] = modPath;
                if (releaseIndex != -1)
                {
                    args[releaseIndex] = release;
                }

                args[paramIndex] = param;
                return (TMod)deleg.DynamicInvoke(args)!;
            };
        }
    }
}
