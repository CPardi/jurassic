using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Jurassic
{
    using System.IO;
    using System.Reflection;
    using System.Reflection.Emit;
    using Compiler;
    using Library;

    public class ScriptCompiler
    {
        private ScriptEngine _engine;

        private List<string> codesInputs = new List<string>();

        public ScriptCompiler()
        {
            _engine = new ScriptEngine
            {
                EnableDebugging = true 
            };
        }

        public void IncludeInput(string code)
        {
            codesInputs.Add(code);
        }

        public void Save(string dllPath)
        {
            var fullpath = Path.GetFullPath(dllPath);

            var assemblyName = Path.GetFileNameWithoutExtension(fullpath);
            var assemblyBuilder =
                AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName(assemblyName),
                    AssemblyBuilderAccess.RunAndSave, 
                    Path.GetDirectoryName(fullpath));

            var module = assemblyBuilder.DefineDynamicModule("Module", Path.GetFileName(fullpath), true);
            _engine.ReflectionEmitInfo = new ScriptEngine.ReflectionEmitModuleInfo() { AssemblyBuilder = assemblyBuilder, ModuleBuilder = module };
            
            foreach (var code in codesInputs)
            {
                _engine.Execute(code);
            }

            var userType = module.DefineType(assemblyName, TypeAttributes.Public);
            var restoreMethod = userType.DefineMethod("RestoreScriptEngine", MethodAttributes.Public | MethodAttributes.Static, typeof(ScriptEngine), new Type[] { });
            var restoreMethodBody = restoreMethod.GetILGenerator();

            // Argument : ScriptEngine
            restoreMethodBody.Emit(OpCodes.Newobj, typeof(ScriptEngine).GetConstructor(new Type[] { }));

            // Argument : ObjectScope
            restoreMethodBody.Emit(OpCodes.Ldnull);
            restoreMethodBody.Emit(OpCodes.Ldnull);
            restoreMethodBody.EmitCall(OpCodes.Callvirt, typeof(ScriptEngine).GetMethod("get_Global"), new Type[] { });
            restoreMethodBody.Emit(OpCodes.Ldc_I4_0);
            restoreMethodBody.Emit(OpCodes.Ldc_I4_0);
            restoreMethodBody.EmitCall(OpCodes.Call, typeof(ObjectScope).GetMethod("CreateRuntimeScope"), new Type[] { });

            // Argument: obj
            restoreMethodBody.EmitCall(OpCodes.Callvirt, typeof(ScriptEngine).GetMethod("get_Global"), new Type[] { });

            restoreMethodBody.EmitCall(OpCodes.Call, module.GetType("JavaScriptClass0").GetMethod("global_"), new Type[] { });
            restoreMethodBody.Emit(OpCodes.Ret);
            userType.CreateType();

            assemblyBuilder.Save(Path.GetFileName(dllPath));
        }

        public ScriptEngine Load(Func<ScriptEngine, Scope, object, object>[] globals, FunctionDelegate[] testRef)
        {
            var engine = new ScriptEngine();
            GeneratedMethod.generatedMethodCache = new Dictionary<long, WeakReference>();

            GeneratedMethod.generatedMethodID = 0;
            foreach (var func in testRef)
            {
                GeneratedMethod.generatedMethodCache.Add(GeneratedMethod.generatedMethodID, new WeakReference(new GeneratedMethod(func, null)));
                GeneratedMethod.generatedMethodID++;
            }

            var globalScope = Jurassic.Compiler.ObjectScope.CreateRuntimeScope(null, engine.Global, false, false);


            object obj = engine.Global;
            foreach (var global in globals)
            {
                global(engine, globalScope, obj);
            }
            
            return engine;
        }
    }
}
