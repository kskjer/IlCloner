using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using IlCloningGenerator;

namespace IlCloningTests
{
    [TestClass]
    public class CopyConstructorTests
    {
        private class TestClass
        {
            public string PropA { get; set; }
            public int PropB { get; set; }
            public DateTime PropC { get; set; }
            public double FieldA { get; set; }

            public virtual List<string> VirtA { get; set; }

            public static TestClass ExampleInstance =>
                new TestClass
                {
                    FieldA = 1234.5,
                    PropA = "Test",
                    PropB = 6666,
                    PropC = DateTime.Now,
                    VirtA = new List<string>
                    {
                        "A", "B", "C", "D"
                    }
                };
        }

        [TestMethod]
        public void TestCreateAndCopy()
        {
            var creator = new IlCopyConstructor();
            var obj = TestClass.ExampleInstance;

            var copier = creator.CreateCopyConstructor(obj);

            var blankWoVirt = new TestClass();
            copier(obj, blankWoVirt, false);

            Assert.IsTrue(obj.FieldA == blankWoVirt.FieldA);
            Assert.IsTrue(obj.PropA == blankWoVirt.PropA);
            Assert.IsTrue(obj.PropB == blankWoVirt.PropB);
            Assert.IsTrue(obj.PropC == blankWoVirt.PropC);
            Assert.IsTrue(blankWoVirt.VirtA == null);

            var blankWithVirt = new TestClass();
            copier(obj, blankWithVirt, true);

            Assert.IsTrue(obj.FieldA == blankWithVirt.FieldA);
            Assert.IsTrue(obj.PropA == blankWithVirt.PropA);
            Assert.IsTrue(obj.PropB == blankWithVirt.PropB);
            Assert.IsTrue(obj.PropC == blankWithVirt.PropC);
            Assert.IsTrue(obj.VirtA == blankWithVirt.VirtA);
        }

        private class TestClassWithCtor
        {
            private CopyConstructorDelegate<TestClassWithCtor> _copier =
                IlCopyConstructor.Default.CreateCopyConstructor<TestClassWithCtor>();

            public string PropA { get; set; }
            public int PropB { get; set; }
            public DateTime PropC { get; set; }
            public double FieldA { get; set; }

            public virtual List<string> VirtA { get; set; }

            public TestClassWithCtor() { }

            public TestClassWithCtor(TestClassWithCtor @base, bool copyVirtual)
            {
                _copier(@base, this, copyVirtual);
            }

            public static TestClassWithCtor ExampleInstance =>
                new TestClassWithCtor
                {
                    FieldA = 1234.5,
                    PropA = "Test",
                    PropB = 6666,
                    PropC = DateTime.Now,
                    VirtA = new List<string>
                    {
                        "A", "B", "C", "D"
                    }
                };
        }

        [TestMethod]
        public void TestAsClass()
        {
            var obj = TestClassWithCtor.ExampleInstance;
            var newObj = new TestClassWithCtor(obj, false);

            Assert.IsTrue(obj.FieldA == newObj.FieldA);
            Assert.IsTrue(obj.PropA == newObj.PropA);
            Assert.IsTrue(obj.PropB == newObj.PropB);
            Assert.IsTrue(obj.PropC == newObj.PropC);
            Assert.IsTrue(newObj.VirtA == null);

            var newObjWithVirt = new TestClassWithCtor(obj, true);

            Assert.IsTrue(obj.FieldA == newObjWithVirt.FieldA);
            Assert.IsTrue(obj.PropA == newObjWithVirt.PropA);
            Assert.IsTrue(obj.PropB == newObjWithVirt.PropB);
            Assert.IsTrue(obj.PropC == newObjWithVirt.PropC);
            Assert.IsTrue(obj.VirtA == newObjWithVirt.VirtA);
        }
    }
}
