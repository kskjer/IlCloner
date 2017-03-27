using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IlCloningGenerator.Tests
{
    public static class Cmp
    {
        public static bool Do<TType, TField>(TType a, TType b, Func<TType, TField> selector)
            where TField : class
        {
            TField valA = selector(a),
                   valB = selector(b);

            if (valA == null || valB == null)
                return valA == valB;
            else
                return valA.Equals(valB);
        }

        public static bool Do<TType, TField>(TType a, TType b, Func<TType, TField> selector, Func<TField, TField, bool> comparer)
            where TField : class
        {
            TField valA = selector(a),
                   valB = selector(b);

            if (valA == null || valB == null)
                return valA == valB;
            else
                return comparer(valA, valB);
        }

        public static bool DoVal<TType, TField>(TType a, TType b, Func<TType, TField> selector) where TField : struct =>
            selector(a).Equals(selector(b));
    }

    public class ExampleInheritingList : List<string>
    {
        public string text;

        public ExampleInheritingList()
        {

        }

        public ExampleInheritingList(string text)
        {
            this.text = text;
        }

        public override bool Equals(object obj)
        {
            var a = this;
            var b = obj as ExampleInheritingList;

            return a.text == b.text &&
                a.SequenceEqual(b);
        }

        public ExampleInheritingList Clone()
        {
            var rval = new ExampleInheritingList
            {
                text = text
            };

            foreach (var i in this)
                rval.Add(i);

            return rval;
        }
    }

    public class ExampleInheritingDictionary : Dictionary<string, string>
    {
        public string text;

        public ExampleInheritingDictionary()
        {

        }

        public ExampleInheritingDictionary(string text)
        {
            this.text = text;
        }

        public override bool Equals(object obj)
        {
            var a = this;
            var b = obj as ExampleInheritingDictionary;

            return a.text == b.text &&
                a.SequenceEqual(b);
        }

        public ExampleInheritingDictionary Clone()
        {
            var rval = new ExampleInheritingDictionary
            {
                text = text
            };

            foreach (var i in this)
                ((ICollection<KeyValuePair<string, string>>)rval).Add(i);

            return rval;
        }
    }

    public struct ExampleStruct
    {
        public int number;
        public string text;
        public ExampleClass nestedClass;

        public override bool Equals(object obj)
        {
            var a = this;
            var b = (ExampleStruct)obj;

            return a.number == b.number &&
                a.text == b.text &&
                Cmp.Do(a, b, (s) => s.nestedClass);
        }

        public ExampleStruct Clone()
        {
            return new ExampleStruct
            {
                number = number,
                text = text,
                nestedClass = nestedClass?.Clone()
            };
        }
    }

    public class ExampleClass : ICloneable
    {
        public override bool Equals(object obj)
        {
            var a = this;
            var b = obj as ExampleClass;

            return new Func<bool>[]
            {
                () => a.text == b.text,
                () => Cmp.Do(a, b, s => s.list, (x, y) => x.SequenceEqual(y)),
                () => Cmp.Do(a, b, s => s.dictionary, (x, y) => x.SequenceEqual(y)),
                () => Cmp.Do(a, b, s => s.dictionaryWithList, (x, y) => 
                        x.Zip(y, (t, v) => 
                            t.Key == v.Key && t.Value.SequenceEqual(v.Value)
                        ).All(p => p)
                    ),
                () => Cmp.Do(a, b, s => s.dictionaryWithListNested, (x, y) =>
                    x.Zip(y, (t, v) => 
                        t.Key == v.Key && t.Value.Zip(v.Value, (e, r) => 
                            Cmp.Do(e, r, q => q)
                        ).All(l => l)
                    ).All(p => p)),
                () => Cmp.Do(a, b, s => s.inheritedList),
                //() => a.inheritedList.Equals(b.inheritedList),
                () => Cmp.Do(a, b, s => s.inheritedDictionary),
                //() => a.inheritedDictionary.Equals(b.inheritedDictionary),
                () => Cmp.Do(a, b, s => s.nested),
                //() => a.nested == b.nested,
                () => Cmp.DoVal(a, b, s => s.nestedStruct)
                //() => a.nestedStruct.Equals(b.nestedStruct)
            }
            .All(t => t());
        }

        public string text;

        public List<string> list;
        public Dictionary<string, string> dictionary;
        public Dictionary<string, List<string>> dictionaryWithList;
        public Dictionary<string, List<ExampleClass>> dictionaryWithListNested;
        public ExampleInheritingList inheritedList;
        public ExampleInheritingDictionary inheritedDictionary;

        public ExampleClass nested;
        public ExampleStruct nestedStruct;

        public ExampleClass Clone()
        {
            var rval = new ExampleClass
            {
                text = text,
                list = list != null ? new List<string>() : null,
                dictionary = dictionary != null ? new Dictionary<string, string>() : null,
                dictionaryWithList = dictionaryWithList != null ? new Dictionary<string, List<string>>() : null,
                dictionaryWithListNested = dictionaryWithListNested != null ? new Dictionary<string, List<ExampleClass>>() : null,
                inheritedDictionary = inheritedDictionary?.Clone(),
                inheritedList = inheritedList?.Clone(),
                nested = nested?.Clone(),
                nestedStruct = nestedStruct.Clone()
            };


            if (list != null)
                foreach (var l in list)
                    rval.list.Add(l);

            if (dictionary != null)
                foreach (var k in dictionary)
                    rval.dictionary.Add(k.Key, k.Value);

            if (dictionaryWithList != null)
                foreach (var k in dictionaryWithList)
                {
                    var val = new List<string>();

                    foreach (var j in k.Value)
                        val.Add(j);

                    rval.dictionaryWithList.Add(k.Key, val);
                }

            if (dictionaryWithListNested != null)
                foreach (var k in dictionaryWithListNested)
                {
                    var val = k.Value
                        .Select(v => v.Clone())
                        .ToList();

                    rval.dictionaryWithListNested.Add(k.Key, val);
                }

            return rval;
        }

        public static ExampleClass GenerateExampleObject() =>
            new ExampleClass
            {
                list = new List<string> { "1", "2", "3", "4" },
                dictionary = new Dictionary<string, string> { { "a", "0" }, { "b", "0" } },
                dictionaryWithList = new Dictionary<string, List<string>>
                {
                    { "x", new List<string> { "5", "6", "7", "8" } },
                    { "y", new List<string> { "4", "3", "2", "1" } },
                },
                dictionaryWithListNested = new Dictionary<string, List<ExampleClass>>
                {
                    {
                        "q",
                        new List<ExampleClass>
                        {
                            new ExampleClass { text = "example-a" },
                            new ExampleClass { text = "example-b" }
                        }
                    },
                    {
                        "w",
                        new List<ExampleClass>
                        {
                            new ExampleClass { text = "example-c" },
                            new ExampleClass { text = "example-d" }
                        }
                    },
                },
                inheritedList = new ExampleInheritingList("list-test") { "p", "o", "i", "u" },
                inheritedDictionary = new ExampleInheritingDictionary("dict-test")
                {
                    { "m", "k" },
                    { "n", "j" },
                },
                nested = new ExampleClass
                {
                    text = "nested-class",
                    nestedStruct = new ExampleStruct
                    {
                        number = 9876,
                        text = "nested-struct"
                    }
                },
                nestedStruct = new ExampleStruct
                {
                    number = 3456,
                    text = "nested-struct-2"
                },
                text = "outer class"
            };

        object ICloneable.Clone() => Clone();
    }



    public class AsList : CollectionBase, IEnumerable<string>
    {
        IEnumerator<string> IEnumerable<string>.GetEnumerator() =>
            InnerList.Cast<string>().GetEnumerator();

        public void Add(string s) =>
            ((IList)this).Add(s);
    }


    public class AsOldSchoolList : CollectionBase
    {
        public AsOldSchoolList2 Nested { get; set; }
    }

    public class AsOldSchoolList2 : CollectionBase
    {
    }

    public class ExampleOldSchoolList : CollectionBase, ICloneable
    {
        public class Item : ICloneable
        {
            public string FieldA { get; set; }
            public string FieldB { get; set; }
            public string FieldC { get; set; }

            public object Clone()
            {
                return new Item
                {
                    FieldA = FieldA,
                    FieldB = FieldB,
                    FieldC = FieldC
                };
            }
        }

        object ICloneable.Clone()
        {
            var rval = new ExampleOldSchoolList() as IList;
            var max = this.Count;
            var self = this as IList;

            for (var i = 0; i < max; i++)
                rval.Add((self[i] as Item).Clone());

            return rval;
        }

        public static ExampleOldSchoolList GenerateExampleObject()
        {
            var rval = new ExampleOldSchoolList();

            for (var i = 0; i < 10; i++)
                (rval as IList).Add(
                    new Item
                    {
                        FieldA = "asdf",
                        FieldB = "1234",
                        FieldC = "xxxx"
                    }
                );

            return rval;
        }
    }
}
