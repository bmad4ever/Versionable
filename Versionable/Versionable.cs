using System.Runtime.CompilerServices;
using System;

namespace Versionable
{
    [AttributeUsage(AttributeTargets.Struct)]
    public class VersionableAttribute : Attribute { }

#if NET7_0_OR_GREATER

/// <summary>
/// Wraps a struct so that each time it is altered via the Versioned wrapper its VERSION is incremented.
/// </summary>
/// <typeparam name="T"></typeparam>
public ref struct Versioned<T> where T : struct
{
    ref T data;

    public int VERSION { get; private set; }
    public T Peek => data;


    public delegate void UpdateFunction(ref T item);

    public void Update(UpdateFunction update_func)
    {
        VERSION++;
        update_func(ref data);
    }

    public static Versioned<T> operator |(Versioned<T> item, UpdateFunction update_func)
    {
        item.Update(update_func);
        return item;
    }

    public Versioned(ref T data)
    {
        this.data = ref data;
        VERSION = 0;
    }
}


#else

    /// <summary>
    /// Wraps a struct so that each time it is altered via the Versioned wrapper its VERSION is incremented.
    /// </summary>
    public unsafe struct Versioned<T> where T : struct
    {
        private readonly T* data;  // Pointer to the T instance
        public int VERSION { get; private set; }

        public delegate void UpdateFunction(ref T item);

        public T Peek => Unsafe.AsRef<T>(data);


        public void Update(UpdateFunction update_func)
        {
            VERSION++;
            update_func(ref Unsafe.AsRef<T>(data));
        }

        public static Versioned<T> operator |(Versioned<T> item, UpdateFunction update_func)
        {
            item.Update(update_func);
            return item;
        }

        public Versioned(ref T data)
        {
            fixed (T* dataPtr = &data)
            {
                this.data = dataPtr;
            }
            VERSION = 0;
        }
    }

#endif
}