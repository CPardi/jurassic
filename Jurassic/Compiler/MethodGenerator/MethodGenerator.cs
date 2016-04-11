﻿using System;
using System.Collections.Generic;

namespace Jurassic.Compiler
{
    /// <summary>
    /// Represents the unit of compilation.
    /// </summary>
    internal abstract class MethodGenerator
    {
        /// <summary>
        /// Creates a new MethodGenerator instance.
        /// </summary>
        /// <param name="engine"> The script engine. </param>
        /// <param name="scope"> The initial scope. </param>
        /// <param name="source"> The source of javascript code. </param>
        /// <param name="options"> Options that influence the compiler. </param>
        protected MethodGenerator(ScriptEngine engine, Scope scope, ScriptSource source, CompilerOptions options)
        {
            if (engine == null)
                throw new ArgumentNullException("engine");
            if (scope == null)
                throw new ArgumentNullException("scope");
            if (source == null)
                throw new ArgumentNullException("source");
            if (options == null)
                throw new ArgumentNullException("options");
            this.Engine = engine;
            this.InitialScope = scope;
            this.Source = source;
            this.Options = options;
            this.StrictMode = this.Options.ForceStrictMode;
        }

        /// <summary>
        /// Gets a reference to the script engine.
        /// </summary>
        public ScriptEngine Engine
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets a reference to any compiler options.
        /// </summary>
        public CompilerOptions Options
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the source of javascript code.
        /// </summary>
        public ScriptSource Source
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets a value that indicates whether strict mode is enabled.
        /// </summary>
        public bool StrictMode
        {
            get;
            protected set;
        }

        /// <summary>
        /// Gets the top-level scope associated with the context.
        /// </summary>
        public Scope InitialScope
        {
            get;
            protected set;
        }

        /// <summary>
        /// Gets the root node of the abstract syntax tree.  This will be <c>null</c> until Parse()
        /// is called.
        /// </summary>
        public Statement AbstractSyntaxTree
        {
            get;
            protected set;
        }

        /// <summary>
        /// Gets or sets optimization information.
        /// </summary>
        public MethodOptimizationHints MethodOptimizationHints
        {
            get;
            set;
        }

        /// <summary>
        /// Gets the generated IL.  This will be <c>null</c> until GenerateCode() is
        /// called.
        /// </summary>
        public ILGenerator ILGenerator
        {
            get;
            protected set;
        }

        /// <summary>
        /// Gets a delegate to the emitted dynamic method, plus any dependencies.  This will be
        /// <c>null</c> until GenerateCode() is called.
        /// </summary>
        public GeneratedMethod GeneratedMethod
        {
            get;
            protected set;
        }

        /// <summary>
        /// Gets a name for the generated method.
        /// </summary>
        /// <returns> A name for the generated method. </returns>
        protected abstract string GetMethodName();

        /// <summary>
        /// Gets a name for the function, as it appears in the stack trace.
        /// </summary>
        /// <returns> A name for the function, as it appears in the stack trace, or <c>null</c> if
        /// this generator is generating code in the global scope. </returns>
        protected virtual string GetStackName()
        {
            return null;
        }

        /// <summary>
        /// Gets an array of types - one for each parameter accepted by the method generated by
        /// this context.
        /// </summary>
        /// <returns> An array of parameter types. </returns>
        protected virtual Type[] GetParameterTypes()
        {
            return new Type[] {
                typeof(ScriptEngine),   // The script engine.
                typeof(Scope),          // The scope.
                typeof(object),         // The "this" object.
            };
        }

        /// <summary>
        /// Parses the source text into an abstract syntax tree.
        /// </summary>
        public abstract void Parse();

        /// <summary>
        /// Optimizes the abstract syntax tree.
        /// </summary>
        public void Optimize()
        {
        }

        /// <summary>
        /// Generates IL for the script.
        /// </summary>
        public void GenerateCode()
        {
            // Generate the abstract syntax tree if it hasn't already been generated.
            if (this.AbstractSyntaxTree == null)
            {
                Parse();
                Optimize();
            }

            // Initialize global code-gen information.
            var optimizationInfo = new OptimizationInfo(this.Engine);
            optimizationInfo.AbstractSyntaxTree = this.AbstractSyntaxTree;
            optimizationInfo.StrictMode = this.StrictMode;
            optimizationInfo.MethodOptimizationHints = this.MethodOptimizationHints;
            optimizationInfo.FunctionName = this.GetStackName();
            optimizationInfo.Source = this.Source;

            ILGenerator generator;
            if (this.Options.EnableDebugging == false)
            {
                // DynamicMethod requires full trust because of generator.LoadMethodPointer in the
                // FunctionExpression class.

                // Create a new dynamic method.
                System.Reflection.Emit.DynamicMethod dynamicMethod;
#if !SILVERLIGHT
                if (ScriptEngine.LowPrivilegeEnvironment == false)
                {
                    // High privilege path.
                    dynamicMethod = new System.Reflection.Emit.DynamicMethod(
                        GetMethodName(),                                        // Name of the generated method.
                        typeof(object),                                         // Return type of the generated method.
                        GetParameterTypes(),                                    // Parameter types of the generated method.
                        typeof(MethodGenerator),                                // Owner type.
                        true);                                                  // Skip visibility checks.
                    // TODO: Figure out why long methods give BadImageFormatException in .NET 3.5 when generated using DynamicILInfo.
                    if (Environment.Version.Major >= 4 && !ScriptEngine.IsMonoRuntime)
                        generator = new DynamicILGenerator(dynamicMethod);
                    else
                        generator = new ReflectionEmitILGenerator(dynamicMethod.GetILGenerator());
                }
                else
                {
#endif
                // Low privilege path.
                dynamicMethod = new System.Reflection.Emit.DynamicMethod(
                    GetMethodName(),                                        // Name of the generated method.
                    typeof(object),                                         // Return type of the generated method.
                    GetParameterTypes());                                   // Parameter types of the generated method.
                generator = new ReflectionEmitILGenerator(dynamicMethod.GetILGenerator());
#if !SILVERLIGHT
                }
#endif

                if (this.Engine.EnableILAnalysis == true)
                {
                    // Replace the generator with one that logs.
                    generator = new LoggingILGenerator(generator);
                }

                // Initialization code will appear to come from line 1.
                optimizationInfo.MarkSequencePoint(generator, new SourceCodeSpan(1, 1, 1, 1));

                // Generate the IL.
                GenerateCode(generator, optimizationInfo);
                generator.Complete();

                // Create a delegate from the method.
                this.GeneratedMethod = new GeneratedMethod(dynamicMethod.CreateDelegate(GetDelegate()), optimizationInfo.NestedFunctions);

            }
            else
            {
#if WINDOWS_PHONE
                throw new NotImplementedException();
#else
                // Debugging or low trust path.
                ScriptEngine.ReflectionEmitModuleInfo reflectionEmitInfo = this.Engine.ReflectionEmitInfo;
                if (reflectionEmitInfo == null)
                {
                    reflectionEmitInfo = new ScriptEngine.ReflectionEmitModuleInfo();

                    // Create a dynamic assembly and module.
                    reflectionEmitInfo.AssemblyBuilder = System.Threading.Thread.GetDomain().DefineDynamicAssembly(
                        new System.Reflection.AssemblyName("Jurassic Dynamic Assembly"), System.Reflection.Emit.AssemblyBuilderAccess.Run);

                    // Mark the assembly as debuggable.  This must be done before the module is created.
                    var debuggableAttributeConstructor = typeof(System.Diagnostics.DebuggableAttribute).GetConstructor(
                        new Type[] { typeof(System.Diagnostics.DebuggableAttribute.DebuggingModes) });
                    reflectionEmitInfo.AssemblyBuilder.SetCustomAttribute(
                        new System.Reflection.Emit.CustomAttributeBuilder(debuggableAttributeConstructor,
                            new object[] { 
                                System.Diagnostics.DebuggableAttribute.DebuggingModes.DisableOptimizations | 
                                System.Diagnostics.DebuggableAttribute.DebuggingModes.Default }));

                    // Create a dynamic module.
                    reflectionEmitInfo.ModuleBuilder = reflectionEmitInfo.AssemblyBuilder.DefineDynamicModule("Module", this.Options.EnableDebugging);

                    this.Engine.ReflectionEmitInfo = reflectionEmitInfo;
                }

                // Create a new type to hold our method.
                var typeBuilder = reflectionEmitInfo.ModuleBuilder.DefineType("JavaScriptClass" + reflectionEmitInfo.TypeCount.ToString(), System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);
                reflectionEmitInfo.TypeCount++;

                // Create a method.
                var methodBuilder = typeBuilder.DefineMethod(this.GetMethodName(),
                    System.Reflection.MethodAttributes.HideBySig | System.Reflection.MethodAttributes.Static | System.Reflection.MethodAttributes.Public,
                    typeof(object), GetParameterTypes());

                // Generate the IL for the method.
                generator = new ReflectionEmitILGenerator(methodBuilder.GetILGenerator());

                if (this.Engine.EnableILAnalysis == true)
                {
                    // Replace the generator with one that logs.
                    generator = new LoggingILGenerator(generator);
                }

                if (this.Source.Path != null && this.Options.EnableDebugging == true)
                {
                    // Initialize the debugging information.
                    optimizationInfo.DebugDocument = reflectionEmitInfo.ModuleBuilder.DefineDocument(this.Source.Path, COMHelpers.LanguageType, COMHelpers.LanguageVendor, COMHelpers.DocumentType);
                    methodBuilder.DefineParameter(1, System.Reflection.ParameterAttributes.None, "scriptEngine");
                    methodBuilder.DefineParameter(2, System.Reflection.ParameterAttributes.None, "scope");
                    methodBuilder.DefineParameter(3, System.Reflection.ParameterAttributes.None, "thisValue");
                }
                optimizationInfo.MarkSequencePoint(generator, new SourceCodeSpan(1, 1, 1, 1));
                GenerateCode(generator, optimizationInfo);
                generator.Complete();

                // Add method id to type.
                var mdGetId = typeBuilder.DefineMethod("GetFunctionId", System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static, typeof(long), new Type[] { });
                var genGetId = new ReflectionEmitILGenerator(mdGetId.GetILGenerator());
                genGetId.LoadInt64(GeneratedMethod.generatedMethodID);
                genGetId.Return();

                var dependGetId = typeBuilder.DefineMethod("GetDependencyIds", System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static, typeof(long[]), new Type[] { });
                var gendependGetId = new ReflectionEmitILGenerator(dependGetId.GetILGenerator());

                var arr = gendependGetId.CreateTemporaryVariable(typeof(long[]));
                gendependGetId.LoadInt32(GeneratedMethod.idCache.Length);
                
                gendependGetId.NewArray(typeof(long));
                gendependGetId.StoreVariable(arr);

                var i = 0;
                foreach (var depend in GeneratedMethod.idCache)
                {
                    gendependGetId.LoadVariable(arr);
                    gendependGetId.LoadInt32(i);
                    gendependGetId.LoadInt64(depend);
                    gendependGetId.StoreArrayElement(typeof(long));
                    i++;
                }

                gendependGetId.LoadVariable(arr);
                gendependGetId.Return();
                

                // Bake it.
                var type = typeBuilder.CreateType();
                var methodInfo = type.GetMethod(this.GetMethodName());
                this.GeneratedMethod = new GeneratedMethod(Delegate.CreateDelegate(GetDelegate(), methodInfo), optimizationInfo.NestedFunctions);
#endif //WINDOWS_PHONE
            }

            if (this.Engine.EnableILAnalysis == true)
            {
                // Store the disassembled IL so it can be retrieved for analysis purposes.
                this.GeneratedMethod.DisassembledIL = generator.ToString();
            }
        }

        /// <summary>
        /// Generates IL for the script.
        /// </summary>
        /// <param name="generator"> The generator to output the CIL to. </param>
        /// <param name="optimizationInfo"> Information about any optimizations that should be performed. </param>
        protected abstract void GenerateCode(ILGenerator generator, OptimizationInfo optimizationInfo);

        /// <summary>
        /// Verifies the scope has the same structure at runtime as at compile time.
        /// </summary>
        /// <param name="generator"> The generator to output the CIL to. </param>
        [System.Diagnostics.Conditional("DEBUG")]
        protected void VerifyScope(ILGenerator generator)
        {
            //// Get the top-level scope.
            //EmitHelpers.LoadScope(generator);
            //var scope = this.InitialScope;

            //while (scope != null)
            //{
            //    // if (scope == null)
            //    //   throw new JavaScriptException()
            //    generator.Duplicate();
            //    var endOfIf1 = generator.CreateLabel();
            //    generator.BranchIfNotNull(endOfIf1);
            //    EmitHelpers.EmitThrow(generator, "Error", "Internal error: runtime scope chain is too short");
            //    generator.DefineLabelPosition(endOfIf1);

            //    // if ((scope is DeclarativeScope/ObjectScope) == false)
            //    //   throw new JavaScriptException()
            //    generator.IsInstance(scope.GetType());
            //    generator.Duplicate();
            //    var endOfIf2 = generator.CreateLabel();
            //    generator.BranchIfNotNull(endOfIf2);
            //    EmitHelpers.EmitThrow(generator, "Error", string.Format("Internal error: incorrect runtime scope type (expected {0})", scope.GetType().Name));
            //    generator.DefineLabelPosition(endOfIf2);

            //    // scope = scope.ParentScope
            //    generator.Call(ReflectionHelpers.Scope_ParentScope);
            //    scope = scope.ParentScope;
            //}

            //// if (scope != null)
            ////   throw new JavaScriptException()
            //var endOfIf3 = generator.CreateLabel();
            //generator.BranchIfNull(endOfIf3);
            //EmitHelpers.EmitThrow(generator, "Error", "Internal error: runtime scope chain is too long");
            //generator.DefineLabelPosition(endOfIf3);
        }

        /// <summary>
        /// Retrieves a delegate for the generated method.
        /// </summary>
        /// <returns> The delegate type that matches the method parameters. </returns>
        protected virtual Type GetDelegate()
        {
            return typeof(Func<ScriptEngine, Scope, object, object>);
        }
    }

}