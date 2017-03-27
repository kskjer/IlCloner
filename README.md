# Introduction

IlCloner is a .NET library that generates IL at runtime for deep cloning 
objects. This is accomplished using [Reflection.Emit](https://msdn.microsoft.com/en-us/library/system.reflection.emit(v=vs.110).aspx).
There is no restriction on depth, and types may contain instances of themselves.

# Examples

## Basic usage

```csharp
using System;
using IlCloningGenerator;

public class ExampleClass : ICloneable
{
    private static readonly Func<ExampleClass, ExampleClass> _cloner =
        IlCloner.CreateCloner<ExampleClass>();
        
    public object Clone() =>
        _cloner(this);
    
    // Remainder of the class definition goes here...
}
```


## Exclusions & straight-copies

The IlCloner class provides a fluent interface for configuration.

```csharp
public class ExampleClassWithExclude : ICloneable
{
    private static readonly Func<ExampleClassWithExclude, ExampleClassWithExclude> _cloner =
        IlCloner.Fluent<ExampleClassWithExclude>()
            // The value of `PropToStraightCopy` will be copied instead of cloned
            .AlwaysStraightCopy(x => x.PropToStraightCopy)
            // The `PropToIgnore` property will not be set at all
            .Exclude(x => x.PropToIgnore)
            .CreateCloner();

    public object Clone() =>
        _cloner(this);

    public byte[] PropToStraightCopy { get; set; }
    public object PropToIgnore { get; set; }

    // Remainder of the class definition goes here...
}
```

# Supported types

 - Classes.
 - Structs.
 - Generic collections implementing ```ICollection<T>``` or ```IList<T>```. This also includes ```Dictionary<TKey, TValue>```.  
 - Non-generic collections (```CollectionBase```, ```DictionaryBase```).
 - Tuples.
 - Key/value pairs.

Fields or properties that are only typed ```object``` can also be cloned. 

# Performance

Generated cloners using this library are faster than those compiled from C#,
even in release mode. This is because the template IL is highly optimized and 
the emitted methods are tagged with the 
```MethodImpl(MethodImplOptions.AggressiveInlining)``` attribute.

The below table shows some results from the Benchmarker project in this repo.

|**Benchmark**|**Clones/sec**|**Improvement over native**|
| --------- | ----------:| -----------------:|
| ```ExampleClass``` IL generated  |  1,024,829.26   | 1.20x          |
| ```ExampleClass``` C# Clone()  |  832,631.22   | 1.00x          |

# License

IlCloner is released under the MIT License.
