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

            var assemblyBuilder =
                AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName(Path.GetFileNameWithoutExtension(fullpath)),
                    AssemblyBuilderAccess.Save, 
                    Path.GetDirectoryName(fullpath),                     
                    null);

            var module = assemblyBuilder.DefineDynamicModule("Module", Path.GetFileName(fullpath), true);
            _engine.ReflectionEmitInfo = new ScriptEngine.ReflectionEmitModuleInfo() { AssemblyBuilder = assemblyBuilder, ModuleBuilder = module };

            foreach (var code in codesInputs)
            {
                _engine.Execute(code);
            }

            assemblyBuilder.Save(Path.GetFileName(dllPath));
        }

        public ScriptEngine Load(Func<ScriptEngine, Scope, object, object> global)
        {
            var engine = new ScriptEngine();
            var globalScope = engine.CreateGlobalScope();
            object obj = null;
            global(engine, globalScope, obj);
            return engine;
        }
    }
}
