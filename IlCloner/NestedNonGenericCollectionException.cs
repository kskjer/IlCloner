using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IlCloningGenerator
{
    public class NestedNonGenericCollectionException : ApplicationException
    {
        public NestedNonGenericCollectionException(string message)
            : base(message)
        {

        }
    }
}
