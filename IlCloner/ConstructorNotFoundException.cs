using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IlCloningGenerator
{
    public class ConstructorNotFoundException : ApplicationException
    {
        public Type Type { get; }

        public ConstructorNotFoundException(string message, Type t)
            : base(message)
        {
            Type = t;
        }
    }
}
