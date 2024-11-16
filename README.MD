### Summary

The class Versioned wraps a struct to keep track of the number of updates done to its data.

The [Versionable] attribute will output a new decorator class that can be swapped with the source data (provided that a defined ctor is used on data initialization or fields are initialized post-creation). 
The generated class keeps track of the number of updates and contains properties to access the struct's fields and properties.

It is also possible to use the generated class using existing data instead of allocating new data, making it an "improved" version of the Versioned class that allows more direct access to the struct's fields and properties.

This is not an all-purpose implementation; it is niche.
If you require events when accessing/modifying fields or properties, these utilities won't be useful.

### Examples

<details>
<summary>
Wrap data to keep track of changes using Versioned class (no type swapping).
</summary>

```csharp
using Versionable;

var container = new NonGenericContainer();
container.itemsA.Update((ref NonGenericContainer.Stock stock) => stock.quantity = 10);
container.itemsA.Update((ref NonGenericContainer.Stock stock) => stock.quantity -= 2);
container.itemsA.Update((ref NonGenericContainer.Stock stock) => stock.quantity -= 3);
container.itemsB|=(ref NonGenericContainer.Stock stock)=> stock.quantity = 5;
Console.WriteLine($"{nameof(container.itemsA)}: {container.itemsA.Peek}; updated {container.itemsA.VERSION} time(s)");
Console.WriteLine($"{nameof(container.itemsB)}: {container.itemsB.Peek}; updated {container.itemsB.VERSION} time(s)");

class NonGenericContainer
{
    public record struct Stock
    {
        public int quantity;
        public int unit_weight;

        public int TotalWeight => quantity * unit_weight;
    }

    private readonly Stock[] stock = new Stock[2];
    public Versioned<Stock> itemsA;
    public Versioned<Stock> itemsB;

    public NonGenericContainer()
    {
        stock[0].unit_weight = 2;
        stock[1].unit_weight = 10;
        itemsA = new Versioned<Stock>(ref stock[0]);
        itemsB = new Versioned<Stock>(ref stock[1]);
    }
}
```

</details>




<details>
<summary>
Generate "versioned" decorator class using the [Versionable] attribute and type swap when debugging.
</summary>

```csharp
// Source data type to make "versionable"
namespace GeometricShapes
{
    [Versionable]
    public struct Circle
    {
        public double radius;
        private double resolution;

        public Circle(double radius, double resolution)
        {
            this.radius = radius;
            this.resolution = resolution;
        }


        public double Diameter
        {
            get => radius * 2;
        }

        public double Area
        {
            get => radius * Math.PI * Math.PI;
            set => radius = value / Math.PI / Math.PI;
        }
    }
}
```

```csharp
// Generated File: GeometricShapes_Circle_Versioned.g.cs
using System;
using Versionable;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
namespace Versionable.Generated_GeometricShapes
{
    using GeometricShapes;

    using Circle = GeometricShapes.Circle;

    public class V_Circle
    {
        private Versioned<Circle> _versioned;
        public int VERSION => _versioned.VERSION;
        private IntPtr? _data;

        public V_Circle(Versioned<Circle> versioned) => _versioned = versioned;
        public V_Circle(ref Circle item) => _versioned = new Versioned<Circle>(ref item);

        public double radius
        {
            get => _versioned.Peek.radius;
            set => _versioned.Update((ref Circle i) => i.radius = value);
        }

        public double Diameter
        {
            get => _versioned.Peek.Diameter;
        }

        public double Area
        {
            get => _versioned.Peek.Area;
            set => _versioned.Update((ref Circle i) => i.Area = value);
        }


        public unsafe V_Circle(double radius, double resolution)
        {
            _data = Marshal.AllocHGlobal(sizeof(Circle));
            Circle* item = (Circle*)_data;
            *item = new Circle(radius, resolution);
            _versioned = new Versioned<Circle>(ref Unsafe.AsRef<Circle>(item));
        }
    

        public unsafe V_Circle()
        {
            _data = Marshal.AllocHGlobal(sizeof(Circle));
            Circle* item = (Circle*)_data;
            *item = new Circle();
            _versioned = new Versioned<Circle>(ref Unsafe.AsRef<Circle>(item));
        }
    
        ~V_Circle() { if (_data.HasValue) Marshal.FreeHGlobal(_data.Value); }
    }
}

```

```csharp
// Replacing Circle with w/ V_Circle
#if DEBUG
using Circle = Versionable.Generated_GeometricShapes.V_Circle;
#else
using Circle = GeometricShapes.Circle;
#endif

var circle = new Circle(radius: 5, resolution: 10);
var multiplier = 2.0d;
circle.radius *= multiplier;
Console.WriteLine($"{nameof(circle)}:" +
    $" {nameof(circle.radius)}={circle.radius}, " +
    $"{nameof(circle.Diameter)}={circle.Diameter}, " +
    $"{nameof(circle.Area)}={circle.Area}");

#if DEBUG
Console.WriteLine($"{nameof(circle)} was modified {circle.VERSION} time(s).");
#endif

```

</details>
