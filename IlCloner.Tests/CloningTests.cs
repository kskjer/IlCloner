using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using IlCloningGenerator;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Collections;

namespace IlCloningGenerator.Tests
{
    [TestClass]
    public class CloningTests
    {
        public class TestNonGenericList : IList
        {
            private ArrayList _inner = new ArrayList();

            public object this[int index]
            {
                get
                {
                    return ((IList)_inner)[index];
                }

                set
                {
                    ((IList)_inner)[index] = value;
                }
            }

            public int Count
            {
                get
                {
                    return ((IList)_inner).Count;
                }
            }

            public bool IsFixedSize
            {
                get
                {
                    return ((IList)_inner).IsFixedSize;
                }
            }

            public bool IsReadOnly
            {
                get
                {
                    return ((IList)_inner).IsReadOnly;
                }
            }

            public bool IsSynchronized
            {
                get
                {
                    return ((IList)_inner).IsSynchronized;
                }
            }

            public object SyncRoot
            {
                get
                {
                    return ((IList)_inner).SyncRoot;
                }
            }

            public int Add(object value)
            {
                return ((IList)_inner).Add(value);
            }

            public void Clear()
            {
                ((IList)_inner).Clear();
            }

            public bool Contains(object value)
            {
                return ((IList)_inner).Contains(value);
            }

            public void CopyTo(Array array, int index)
            {
                ((IList)_inner).CopyTo(array, index);
            }

            public IEnumerator GetEnumerator()
            {
                return ((IList)_inner).GetEnumerator();
            }

            public int IndexOf(object value)
            {
                return ((IList)_inner).IndexOf(value);
            }

            public void Insert(int index, object value)
            {
                ((IList)_inner).Insert(index, value);
            }

            public void Remove(object value)
            {
                ((IList)_inner).Remove(value);
            }

            public void RemoveAt(int index)
            {
                ((IList)_inner).RemoveAt(index);
            }
        }

        public class TestTwoNonGenericMembersSameType
        {
            public TestNonGenericList ListA { get; set; }
            public TestNonGenericList ListB { get; set; }
            public TestNonGenericList ListC { get; set; }
        }


        /// <summary>
        /// The cloner assumes that for any given type, it should be cloned with the same
        /// logic always. This does not hold true for old school collections where the 
        /// contents are simply an object. For every such member or type we encounter, we
        /// must generate new cloner code for it. This test ensures that that happens.
        /// </summary>
        [TestMethod]
        public void TestSameNonGenericTypesClonedDifferently()
        {
            var ilCloner = new IlCloner();

            var example = new TestTwoNonGenericMembersSameType()
            {
                ListA = new TestNonGenericList
                {
                    "a", "b", "c", "d"
                },
                ListB = new TestNonGenericList
                {
                    1, 2, 3, 4
                }
            };

            var cloner = ilCloner.CreateClonerDelegate(example);

            var exampleCloned = cloner(example);

            Assert.IsFalse(Object.ReferenceEquals(example, exampleCloned));
            Assert.IsFalse(Object.ReferenceEquals(example.ListA, exampleCloned.ListA));
            Assert.IsFalse(Object.ReferenceEquals(example.ListB, exampleCloned.ListB));

            Assert.IsTrue(example.ListA.Cast<string>().SequenceEqual(exampleCloned.ListA.Cast<string>()));
            Assert.IsTrue(example.ListB.Cast<int>().SequenceEqual(exampleCloned.ListB.Cast<int>()));
        }



        /// <summary>
        /// This functionality is not currently supported, so this tests expects the cloner
        /// to throw an exception.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(NestedNonGenericCollectionException))]
        public void TestNonGenericListContainingNonGenericListOfSameType()
        {
            var ilCloner = new IlCloner();

            var example = new TestTwoNonGenericMembersSameType()
            {
                ListC = new TestNonGenericList()
            };

            example.ListC.Add(new TestNonGenericList { 1, 2, 3, 4 });

            var cloner = IlCloner.CreateCloner(example);

            var exampleCloned = cloner(example);


            Assert.IsFalse(Object.ReferenceEquals(example.ListC, exampleCloned.ListC));
            Assert.IsFalse(Object.ReferenceEquals(example.ListC[0], exampleCloned.ListC[0]));
        }

        [TestMethod]
        public void TestNonGenericListCloning()
        {
            var ilCloner = new IlCloner();

            var example = new AsList
            {
                "A", "B", "C"
            };

            var cloner = ilCloner.CreateClonerDelegate<AsList>();

            var cloned = cloner(example);

            Assert.IsFalse(Object.ReferenceEquals(example, cloned));
            Assert.IsTrue(example.SequenceEqual(cloned));

            var nonGenList = new ExampleOldSchoolList();
            ((IList)nonGenList).Add(example);
            ((IList)nonGenList).Add(example);
            ((IList)nonGenList).Add(example);
            ((IList)nonGenList).Add(example);
            ((IList)nonGenList).Add(example);
            ((IList)nonGenList).Add(example);

            var clonerNonGen = IlCloner.CreateCloner(nonGenList);
            var clonedNonGen = clonerNonGen(nonGenList);

            Assert.IsFalse(Object.ReferenceEquals(nonGenList, clonedNonGen));
            Assert.IsTrue(example.SequenceEqual(cloned));

            for (var i = 0; i < nonGenList.Count; i++)
                Assert.IsFalse(Object.ReferenceEquals(((IList)nonGenList)[i], ((IList)clonedNonGen)[i]));
        }

        [TestMethod]
        public void TestArrayCloning()
        {
            var ilCloner = new IlCloner();

            var exampleArray = new[]
            {
                Enumerable.Range(0, 10)
                    .Select(i => ExampleClass.GenerateExampleObject())
                    .ToArray()
            }
            .ToArray();

            var cloner = IlCloner.CreateCloner(exampleArray);

            var cloned = cloner(exampleArray);

            Assert.IsFalse(Object.ReferenceEquals(exampleArray, cloned));

            for (var i = 0; i < exampleArray.Length; i++)
            {
                Assert.IsFalse(Object.ReferenceEquals(exampleArray[i], cloned[i]));

                for (var j = 0; j < exampleArray[i].Length; j++)
                {
                    Assert.IsFalse(Object.ReferenceEquals(exampleArray[i][j], cloned[i][j]));
                    Assert.IsTrue(exampleArray[i][j].Equals(cloned[i][j]));
                }
            }
        }

        [TestMethod]
        public void ClassAndStructCloning()
        {
            try
            {
                var ilCloner = new IlCloner();

                var rootAsReferenceType = ExampleClass.GenerateExampleObject();
                var rootAsValueType = ExampleClass.GenerateExampleObject().nestedStruct;

                var clonerRef = ilCloner.CreateClonerDelegate<ExampleClass>();
                var clonerValue = ilCloner.CreateClonerDelegate<ExampleStruct>();

                var clonedReference = clonerRef(rootAsReferenceType);
                var clonedStruct = clonerValue(rootAsValueType);

                Assert.IsTrue(rootAsReferenceType.Equals(clonedReference));
                Assert.IsTrue(rootAsValueType.Equals(clonedStruct));

                Assert.IsTrue(rootAsReferenceType.Equals(rootAsReferenceType.Clone()));
            }
            catch (Exception e)
            {
                var j = e + "";

                throw;
            }
        }

        public class TestNestedStruct_IS_CLASS
        {
            public TestNestedClass_IS_STRUCT StructProp { get; set; }
        }

        public struct TestNestedClass_IS_STRUCT
        {
            public TestNestedStruct_IS_CLASS ClassProp { get; set; }
        }

        [TestMethod]
        public void TestClassWithStructCloning()
        {
            var obj = new TestNestedStruct_IS_CLASS
            {
                StructProp = new TestNestedClass_IS_STRUCT
                {
                    ClassProp = new TestNestedStruct_IS_CLASS
                    {
                        StructProp = new TestNestedClass_IS_STRUCT()
                    }
                }
            };

            var cloner = new IlCloner();

            var clonerClass = cloner.CreateClonerDelegate(obj);
            var clonedClass = clonerClass(obj);
        }


        [TestMethod]
        public void TestStructWithClassCloning()
        {
            var obj = new TestNestedStruct_IS_CLASS
            {
                StructProp = new TestNestedClass_IS_STRUCT
                {
                    ClassProp = new TestNestedStruct_IS_CLASS
                    {
                        StructProp = new TestNestedClass_IS_STRUCT()
                    }
                }
            };

            var cloner = new IlCloner();

            var clonerStruct = cloner.CreateClonerDelegate(obj.StructProp);
            var clonedStruct = clonerStruct(obj.StructProp);
        }


        public struct TestStructOnly
        {
            public string PropA { get; set; }
            public int PropB { get; set; }
        }

        public struct TestStructWithNestedStruct
        {
            public string PropC { get; set; }
            public TestStructOnly Nested { get; set; }
        }

        [TestMethod]
        public void TestStructCloning()
        {
            var obj = new TestStructOnly
            {
                PropA = "ASDF",
                PropB = 1234
            };

            var cloner = new IlCloner();
            var del = cloner.CreateClonerDelegate(obj);

            var output = del(obj);

            Assert.AreEqual(obj.PropA, output.PropA);
            Assert.AreEqual(obj.PropB, output.PropB);
        }

        [TestMethod]
        public void TestNestedStructCloning()
        {
            var obj = new TestStructWithNestedStruct
            {
                Nested = new TestStructOnly
                {
                    PropA = "ASDF",
                    PropB = 1234
                },
                PropC = "ZZZZ"
            };

            var cloner = new IlCloner();
            var del = cloner.CreateClonerDelegate(obj);

            var output = del(obj);

            Assert.AreEqual(obj.Nested.PropA, output.Nested.PropA);
            Assert.AreEqual(obj.Nested.PropB, output.Nested.PropB);
            Assert.AreEqual(obj.PropC, output.PropC);
        }

        public class TestList : IList
        {
            public bool GetIndexerHit { get; private set; }
            public bool SetIndexerHit { get; private set; }

            private readonly ArrayList _inner = new ArrayList();

            public object this[int index]
            {
                get
                {
                    GetIndexerHit = true;

                    return _inner[index];
                }

                set
                {
                    _inner[index] = value;
                }
            }

            public int Count
            {
                get
                {
                    return _inner.Count;
                }
            }

            public bool IsFixedSize
            {
                get
                {
                    return _inner.IsFixedSize;
                }
            }

            public bool IsReadOnly
            {
                get
                {
                    return _inner.IsReadOnly;
                }
            }

            public bool IsSynchronized
            {
                get
                {
                    return _inner.IsSynchronized;
                }
            }

            public object SyncRoot
            {
                get
                {
                    return _inner.SyncRoot;
                }
            }

            public int Add(object value)
            {
                return _inner.Add(value);
            }

            public void Clear()
            {
                _inner.Clear();
            }

            public bool Contains(object value)
            {
                return _inner.Contains(value);
            }

            public void CopyTo(Array array, int index)
            {
                _inner.CopyTo(array, index);
            }

            public IEnumerator GetEnumerator()
            {
                return _inner.GetEnumerator();
            }

            public int IndexOf(object value)
            {
                return _inner.IndexOf(value);
            }

            public void Insert(int index, object value)
            {
                _inner.Insert(index, value);
            }

            public void Remove(object value)
            {
                _inner.Remove(value);
            }

            public void RemoveAt(int index)
            {
                _inner.RemoveAt(index);
            }
        }

        public class TestList<T> : IList<T>
        {
            private readonly List<T> _inner = new List<T>();

            public bool GetIndexerHit { get; private set; }

            public T this[int index]
            {
                get
                {
                    GetIndexerHit = true;

                    return _inner[index];
                }

                set
                {
                    _inner[index] = value;
                }
            }

            public int Count
            {
                get
                {
                    return _inner.Count;
                }
            }

            public bool IsReadOnly
            {
                get
                {
                    return ((IList<T>)_inner).IsReadOnly;
                }
            }

            public void Add(T item)
            {
                _inner.Add(item);
            }

            public void Clear()
            {
                _inner.Clear();
            }

            public bool Contains(T item)
            {
                return _inner.Contains(item);
            }

            public void CopyTo(T[] array, int arrayIndex)
            {
                _inner.CopyTo(array, arrayIndex);
            }

            public IEnumerator<T> GetEnumerator()
            {
                return ((IList<T>)_inner).GetEnumerator();
            }

            public int IndexOf(T item)
            {
                return _inner.IndexOf(item);
            }

            public void Insert(int index, T item)
            {
                _inner.Insert(index, item);
            }

            public bool Remove(T item)
            {
                return _inner.Remove(item);
            }

            public void RemoveAt(int index)
            {
                _inner.RemoveAt(index);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return ((IList<T>)_inner).GetEnumerator();
            }
        }

        [TestMethod]
        public void TestIndexerBeingUsedForIList()
        {
            var ilCloner = new IlCloner();

            var list = new TestList();

            for (var i = 0; i < 4; i++)
                list.Add(i);

            var cloner = IlCloner.CreateCloner(list);
            var newList = cloner(list);

            Assert.IsTrue(list.GetIndexerHit, "Get indexer was not accessed while cloning list");
        }

        [TestMethod]
        public void TestIndexerBeingUsedForIListGeneric()
        {
            var ilCloner = new IlCloner();

            var list = new TestList<int>();

            for (var i = 0; i < 4; i++)
                list.Add(i);

            var cloner = IlCloner.CreateCloner(list);
            var newList = cloner(list);

            Assert.IsTrue(list.GetIndexerHit, "Get indexer was not accessed while cloning list");
        }

        [TestMethod]
        public void TestThreadSafety()
        {
            var ilCloner = new IlCloner();

            var threadCount = Math.Max(4, Environment.ProcessorCount - 1);
            var threadsRunning = 0;
            var start = false;

            var tasks = Enumerable.Range(0, threadCount)
                .Select(x => Task.Run(() => 
                {
                    Interlocked.Increment(ref threadsRunning);
                    SpinWait.SpinUntil(() => start);

                    ilCloner.CreateClonerDelegate<ExampleClass>();
                }))
                .ToArray();

            SpinWait.SpinUntil(() => threadsRunning == threadCount);
            start = true;

            Task.WaitAll(tasks);
        }

        public class TestClassWithoutCtor
        {
            public string PropA { get; set; }

            private TestClassWithoutCtor() { }

            public static TestClassWithoutCtor Create() => new TestClassWithoutCtor();
        }

        [TestMethod]
        [ExpectedException(typeof(ConstructorNotFoundException))]
        public void TestTypeWithoutConstructor()
        {
            var ilCloner = new IlCloner();
            ilCloner.CreateClonerDelegate<TestClassWithoutCtor>();
        }

        [TestMethod]
        public void TestTypeWithCustomConstructor()
        {
            var ilCloner = new IlCloner();
            ilCloner.DefineCustomConstructor<TestClassWithoutCtor>(c => TestClassWithoutCtor.Create());

            var obj = TestClassWithoutCtor.Create();
            obj.PropA = "JJJ";

            ilCloner.CreateClonerDelegate<TestClassWithoutCtor>()(obj);
        }

        public class TestClassWithInitializedMembers
        {
            public TestClassWithConstructCount Prop { get; set; } = new TestClassWithConstructCount();
            public TestClassWithConstructCount Field = new TestClassWithConstructCount();
        }

        public class TestClassWithConstructCount
        {
            public static int Counter;

            public TestClassWithConstructCount()
            {
                Interlocked.Increment(ref Counter);
            }
        }


        [TestMethod]
        public void TestInitializedMembersNotDoublyInitialized()
        {
            var exampleObj = new TestClassWithInitializedMembers();
            var countAtStart = TestClassWithConstructCount.Counter;
            var cloner = new IlCloner().CreateClonerDelegate(exampleObj);

            cloner(exampleObj);

            Assert.AreEqual(2, TestClassWithConstructCount.Counter - countAtStart);
        }

        public class TestCollection : CollectionBase
        {
            public void Add(object b) => ((IList)this).Add(b);
        }

        public class TestCollectionItem
        {
            public TestCollection Contents { get; set; }
            public string Text { get; set; }
        }


        /// <remarks>
        /// Internally, the IlCloner creates delegates that take two parameters and return
        /// the newly cloned object. 
        /// The outer delegate - the one created and returned to the user - only takes one parameter.
        /// This test ensures that a separate delegate is created for cloning nested instances of
        /// the outer type.
        /// </remarks>
        [TestMethod]
        public void TestSeparateDelegateIsCreatedForNestedOuter()
        {
            var ilCloner = new IlCloner();
            var obj = new TestCollectionItem
            {
                Contents = new TestCollection
                {
                    new TestCollectionItem { Text ="ZZZ" },
                    new TestCollectionItem { Text ="XXX" },
                    new TestCollectionItem { Text ="YYY" },
                },
                Text = "ZZZZ"
            };

            var cloner = ilCloner.CreateClonerDelegate(obj);

            /*
IL_0000: ldarg.1    
IL_0001: stloc.0    
IL_0002: ldarg.0    
IL_0003: call       Int32 get_Count()/System.Collections.CollectionBase
IL_0008: stloc.1    
IL_0009: ldc.i4.0   
IL_000a: stloc.2    
IL_000b: br.s       IL_0025
IL_000d: ldloc.0    
IL_000e: ldarg.0    
IL_000f: ldloc.2    
IL_0010: callvirt   System.Object get_Item(Int32)/System.Collections.IList
IL_0015: ldnull     
                    -- Here is the problem, one extra argument
                    -- The cached method was the outer one.
IL_0016: call       TestCollectionItem DeepClone_TestCollectionItem(TestCollectionItem)/DynGen0000_CloneState_TestCollectionItem
IL_001b: callvirt   Int32 Add(System.Object)/System.Collections.IList
IL_0020: pop        
IL_0021: ldloc.2    
IL_0022: ldc.i4.1   
IL_0023: add        
IL_0024: stloc.2    
IL_0025: ldloc.2    
IL_0026: ldloc.1    
IL_0027: blt.s      IL_000d
IL_0029: ret        
            */
            try
            {
                cloner(obj);
            }
            catch (Exception e)
            {
                var k = e + "";
                var j = 0;
            }
        }


        public interface ITestInterface
        {
            string PropA { get; set; }
        }

        public class TestClassWithInterfaceMember
        {
            public ITestInterface PropInterface { get; set; }
        }

        public class TestClassImplementingInterface : ITestInterface
        {
            public string PropA { get; set; }

            public TestClassImplementingInterface()
            {
                string x = " test";
            }
        }


        [TestMethod]
        public void TestInterfaceMember()
        {
            var cloner = new IlCloner();

            var instance = new TestClassWithInterfaceMember
            {
                PropInterface = new TestClassImplementingInterface
                {
                    PropA = "ASDF"
                }
            };

            var del = cloner.CreateClonerDelegate(instance);
            var output = del(instance);

            Assert.IsFalse(Object.ReferenceEquals(instance, output));
            Assert.IsFalse(Object.ReferenceEquals(instance.PropInterface, output.PropInterface));
            Assert.AreEqual(instance.PropInterface.GetType(), output.PropInterface.GetType());
            Assert.AreEqual(instance.PropInterface.PropA, output.PropInterface.PropA);
        }

        public class TestClassWIthAbstractMember
        {
            public TestAbstractClass PropAbstract { get; set; }
        }

        public abstract class TestAbstractClass
        {
            public abstract string PropA { get; set; }
        }

        public class TestClassImplementingAbstract : TestAbstractClass
        {
            public override string PropA { get; set; }
        }


        [TestMethod]
        public void TestAbstractMember()
        {
            var cloner = new IlCloner();

            var instance = new TestClassWIthAbstractMember
            {
                PropAbstract = new TestClassImplementingAbstract
                {
                    PropA = "ASDF"
                }
            };

            var del = cloner.CreateClonerDelegate(instance);
            var output = del(instance);

            Assert.IsFalse(Object.ReferenceEquals(instance, output));
            Assert.IsFalse(Object.ReferenceEquals(instance.PropAbstract, output.PropAbstract));
            Assert.AreEqual(instance.PropAbstract.GetType(), output.PropAbstract.GetType());
            Assert.AreEqual(instance.PropAbstract.PropA, output.PropAbstract.PropA);
        }

        public class TestClassWithDecimal
        {
            public decimal DecimalField = 123456.0M;
            public decimal DecimalProp { get; set; } = 123456.0M;
        }

        [TestMethod]
        public void TestDecimalType()
        {
            var cloner = new IlCloner();
            var obj = new TestClassWithDecimal();
            var del = cloner.CreateClonerDelegate(obj);

            var cloned = del(obj);

            Assert.IsFalse(Object.ReferenceEquals(obj, cloned));
            Assert.AreEqual(obj.DecimalField, cloned.DecimalField);
            Assert.AreEqual(obj.DecimalProp, cloned.DecimalProp);
        }

        private static IlCloner Cloner => new IlCloner();

        [TestMethod]
        public void TestNullValueAddedToDictionary()
        {
            var dict = new Dictionary<string, string>
            {
                { "a", null }
            };

            var cloner = Cloner.CreateClonerDelegate(dict);
            var clonedDict = cloner(dict);

            Assert.IsTrue(clonedDict.ContainsKey("a"));
            Assert.AreEqual(null, clonedDict["a"]);
        }

        [TestMethod]
        public void TestNullValueAddedToNonGenericDictionary()
        {
            var dict = new Hashtable();
            dict["a"] = null;

            var cloner = Cloner.CreateClonerDelegate(dict);
            var clonedDict = cloner(dict);

            Assert.IsTrue(clonedDict.ContainsKey("a"));
            Assert.AreEqual(null, clonedDict["a"]);
        }


        public class TestWithPreinitializedList
        {
            public List<string> Items { get; set; } =
                new List<string>
                {
                    "a",
                    "b",
                    "c",
                    "d",
                    "e"
                };
        }

        [TestMethod]
        public void TestPreinitListDoesntDuplicateItems()
        {
            var obj = new TestWithPreinitializedList();
            var cloner = Cloner.CreateClonerDelegate(obj);
            var cloned = cloner(obj);

            Assert.AreEqual(obj.Items.Count, cloned.Items.Count);
            Assert.IsTrue(obj.Items.SequenceEqual(cloned.Items));
        }


        public class TestClassWithReadonlyField
        {
            public readonly string Test;

            public TestClassWithReadonlyField(string jjj)
            {
                Test = jjj;
            }

            public TestClassWithReadonlyField()
            {

            }
        }

        /// <summary>
        /// The cloner should honor the readonly status of the field and not attempt
        /// to copy the value.
        /// </summary>
        [TestMethod]
        public void TestWithReadonlyField()
        {
            var obj = new TestClassWithReadonlyField("test");
            var cloner = Cloner.CreateClonerDelegate(obj);
            var cloned = cloner(obj);

            Assert.IsNull(cloned.Test);
        }

        [TestMethod]
        public void TestTuples()
        {
            var service = Cloner;
            service.SaveAssemblies = true;

            var obj = Tuple.Create(DateTime.Now, TimeSpan.FromMinutes(1));
            var cloner = service.CreateClonerDelegate(obj);

            var newObj = cloner(obj);

            Assert.IsFalse(Object.ReferenceEquals(obj, newObj));
            Assert.AreEqual(obj.Item1, newObj.Item1);
            Assert.AreEqual(obj.Item2, newObj.Item2);
        }

        [TestMethod]
        public void TestTuplesNested()
        {
            var obj = Tuple.Create(DateTime.Now, TimeSpan.FromMinutes(1), Tuple.Create(1, 2, 3), "test");
            var cloner = Cloner.CreateClonerDelegate(obj);

            var newObj = cloner(obj);

            Assert.IsFalse(Object.ReferenceEquals(obj, newObj));
            Assert.IsFalse(Object.ReferenceEquals(obj.Item3, newObj.Item3));
            Assert.AreEqual(obj.Item1, newObj.Item1);
            Assert.AreEqual(obj.Item2, newObj.Item2);
            Assert.AreEqual(obj.Item3.Item1, newObj.Item3.Item1);
            Assert.AreEqual(obj.Item3.Item2, newObj.Item3.Item2);
            Assert.AreEqual(obj.Item3.Item3, newObj.Item3.Item3);
            Assert.AreEqual(obj.Item4, newObj.Item4);
        }
        public class TestClassWithFieldExclusion
        {
            public string FieldA;
        }

        [TestMethod]
        public void TestFieldExclusion()
        {
            var cloner = new IlClonerFluent<TestClassWithFieldExclusion>()
                .Exclude(x => x.FieldA)
                .CreateCloner();

            var obj = new TestClassWithFieldExclusion()
            {
                FieldA = "ASDF"
            };

            var cloned = cloner(obj);

            Assert.AreNotEqual(obj.FieldA, cloned.FieldA);
            Assert.IsNull(cloned.FieldA);
        }

        public class TestClassWithPropertyExclusion
        {
            public string PropA { get; set; }
        }

        [TestMethod]
        public void TestPropertyExclusion()
        {
            var cloner = new IlClonerFluent<TestClassWithPropertyExclusion>()
                .Exclude(x => x.PropA)
                .CreateCloner();

            var obj = new TestClassWithPropertyExclusion()
            {
                PropA = "ASDF"
            };

            var cloned = cloner(obj);

            Assert.AreNotEqual(obj.PropA, cloned.PropA);
            Assert.IsNull(cloned.PropA);
        }

        public class TestClassWithStraightCopiedProp
        {
            public TestClassWithPropertyExclusion Prop { get; set; }
        }

        [TestMethod]
        public void TestExplicitStraightCopy()
        {
            var obj = new TestClassWithStraightCopiedProp
            {
                Prop = new TestClassWithPropertyExclusion
                {
                    PropA = "ASDF"
                }
            };

            var cloner = IlCloner.Fluent(obj)
                .AlwaysStraightCopy(x => x.Prop)
                .CreateCloner();

            var clonerDeep = Cloner.CreateClonerDelegate(obj);

            var cloned = cloner(obj);

            Assert.IsFalse(Object.ReferenceEquals(obj, cloned));
            Assert.IsTrue(Object.ReferenceEquals(obj.Prop, cloned.Prop));

            var clonedDeep = clonerDeep(obj);

            Assert.IsFalse(Object.ReferenceEquals(obj, clonedDeep));
            Assert.IsFalse(Object.ReferenceEquals(obj.Prop, clonedDeep.Prop));
            Assert.AreEqual(obj.Prop.PropA, clonedDeep.Prop.PropA);
        }
    }
}
