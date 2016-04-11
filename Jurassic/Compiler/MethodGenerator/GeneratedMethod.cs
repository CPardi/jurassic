using System;
using System.Collections.Generic;

namespace Jurassic.Compiler
{
    /// <summary>
    /// Represents a generated method and it's dependencies.  For internal use only.
    /// </summary>
    public class GeneratedMethod
    {
        /// <summary>
        /// Creates a new GeneratedMethod instance.
        /// </summary>
        /// <param name="delegateToMethod"> A delegate that refers to the generated method. </param>
        /// <param name="dependencies"> A list of dependent generated methods.  Can be <c>null</c>. </param>
        public GeneratedMethod(Delegate delegateToMethod, IList<GeneratedMethod> dependencies)
        {
            if (delegateToMethod == null)
                throw new ArgumentNullException("delegateToMethod");
            this.GeneratedDelegate = delegateToMethod;
            this.Dependencies = dependencies;
        }

        /// <summary>
        /// Gets a delegate which refers to the generated method.
        /// </summary>
        public Delegate GeneratedDelegate
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets a list of dependent generated methods.
        /// </summary>
        public IList<GeneratedMethod> Dependencies
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets or sets the disassembled IL code for the method.
        /// </summary>
        public string DisassembledIL
        {
            get;
            set;
        }



        public static Dictionary<long, WeakReference> generatedMethodCache { get; set; }

        public static Dictionary<GeneratedMethod, long> inverseCache { get; set; } = new Dictionary<GeneratedMethod, long>();
        public static long[] idCache { get; set; } = {};
        private static object cacheLock = new object();

        public static long generatedMethodID { get; set; }
        private const int compactGeneratedCacheCount = 100;

        /// <summary>
        /// Retrieves the code for a generated method, given the ID.  For internal use only.
        /// </summary>
        /// <param name="id"> The ID of the generated method. </param>
        /// <returns> A <c>GeneratedMethodInfo</c> instance. </returns>
        public static GeneratedMethod Load(long id)
        {
            lock (cacheLock)
            {
                if (generatedMethodCache == null)
                    throw new InvalidOperationException("Internal error: no generated method cache available.");
                WeakReference generatedMethodReference;
                if (generatedMethodCache.TryGetValue(id, out generatedMethodReference) == false)
                    throw new InvalidOperationException(string.Format("Internal error: generated method {0} was garbage collected.", id));
                var generatedMethod = (GeneratedMethod)generatedMethodReference.Target;
                if (generatedMethod == null)
                    throw new InvalidOperationException(string.Format("Internal error: generated method {0} was garbage collected.", id));
                return generatedMethod;
            }
        }

        /// <summary>
        /// Saves the given generated method and returns an ID.  For internal use only.
        /// </summary>
        /// <param name="generatedMethod"> The generated method to save. </param>
        /// <returns> The ID that was associated with the generated method. </returns>
        public static long Save(GeneratedMethod generatedMethod)
        {
            if (generatedMethod == null)
                throw new ArgumentNullException("generatedMethod");
            lock (cacheLock)
            {
                // Create a cache (if it hasn't already been created).
                if (generatedMethodCache == null)
                    generatedMethodCache = new Dictionary<long, WeakReference>();

                // Create a weak reference to the generated method and add it to the cache.
                long id = generatedMethodID;
                AddToMethodCache(id, generatedMethod);

                // Increment the ID for next time.
                generatedMethodID++;

                // Every X calls to this method, compact the cache by removing any weak references that
                // point to objects that have been collected.
                if (generatedMethodID % compactGeneratedCacheCount == 0)
                {
                    // Remove any weak references that have expired.
                    var expiredIDs = new List<long>();
                    foreach (var pair in generatedMethodCache)
                        if (pair.Value.Target == null)
                            expiredIDs.Add(pair.Key);
                    foreach (int expiredID in expiredIDs)
                        generatedMethodCache.Remove(expiredID);
                }

                // Return the ID that was allocated.
                return id;
            }
        }

        public static void AddToMethodCache(long id, GeneratedMethod generatedMethod)
        {
            inverseCache.Add(generatedMethod, id);
            var weakReference = new WeakReference(generatedMethod);
            generatedMethodCache.Add(id, weakReference);

            if (generatedMethod.Dependencies == null)
            {
                return;
            }
            
            var dependancyList = new List<long>();
            foreach (var method in generatedMethod.Dependencies)
            {
                dependancyList.Add(inverseCache[generatedMethod]);
            }

            idCache = dependancyList.ToArray();
        }
    }
}
