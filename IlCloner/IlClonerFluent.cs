using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IlCloningGenerator
{
    public class IlClonerFluent<T> : IIlClonerFluent<T>
    {
        private readonly IlCloner _cloner;

        public IlClonerFluent(IlCloner cloner)
        {
            _cloner = cloner;
        }

        public IlClonerFluent()
        {
            _cloner = new IlCloner();
        }

        public IlCloner Cloner => _cloner;

        public IIlClonerFluent<T> Exclude<TMember>(Expression<Func<T, TMember>> selector)
        {
            var member = (selector.Body as MemberExpression)?.Member;

            if (member == null)
                throw new ArgumentException();

            var prop = member as PropertyInfo;
            var fld = member as FieldInfo;

            if (prop != null)
                _cloner.Exclude(prop);

            if (fld != null)
                _cloner.Exclude(fld);

            return this;
        }

        public IIlClonerFluent<T> Include<TMember>(Expression<Func<T, TMember>> selector)
        {
            var member = (selector.Body as MemberExpression)?.Member;

            if (member == null)
                throw new ArgumentException();

            var prop = member as PropertyInfo;
            var fld = member as FieldInfo;

            if (prop != null)
                _cloner.Include(prop);

            if (fld != null)
                _cloner.Include(fld);

            return this;
        }

        public IIlClonerFluent<T> AlwaysStraightCopy<TMember>(Expression<Func<T, TMember>> selector)
        {
            var member = (selector.Body as MemberExpression)?.Member;

            if (member == null)
                throw new ArgumentException();

            var prop = member as PropertyInfo;
            var fld = member as FieldInfo;

            if (prop != null)
                _cloner.AlwaysStraightCopy(prop);

            if (fld != null)
                _cloner.AlwaysStraightCopy(fld);

            return this;
        }

        public IIlClonerFluent<TOther> Configure<TOther>() => 
            new IlClonerFluent<TOther>(_cloner);

        public Func<T, T> CreateCloner() => 
            _cloner.CreateClonerDelegate<T>();
    }
}
