using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace IlCloningGenerator
{
    public delegate void CopyConstructorDelegate<T>(T source, T destination, bool copyVirtual);

    public class IlCopyConstructor
    {
        public static IlCopyConstructor Default { get; } = new IlCopyConstructor();


        /// <summary>
        /// Create a delegate that can be used to implement a copy constructor.
        /// The delegate contains a boolean which allows you to specify if you
        /// want to ignore virtual properties.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="instance">This parameter is not used; it's only for type inference if you happen to have an instance around.</param>
        /// <returns></returns>
        public CopyConstructorDelegate<T> CreateCopyConstructor<T>(T instance = default(T))
        {
            var t = typeof(T);
            var dynMethod = new DynamicMethod(
                $"CopyConstructor_{t.Name}",
                typeof(void),
                new[] { t, t, typeof(bool) },
                true
            );

            var generator = dynMethod.GetILGenerator();
            var endOfFunction = generator.DefineLabel();

            var members = t
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.GetGetMethod()?.IsPublic == true && p.GetSetMethod()?.IsPublic == true)
                .Select(p => new { Member = p as MemberInfo, p.GetGetMethod().IsVirtual})
                .Concat(
                    t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Select(f => new { Member = f as MemberInfo, IsVirtual = false })
                );

            Action<IEnumerable<MemberInfo>> iter = (list) =>
            {
                foreach (var m in list)
                {
                    if (m is PropertyInfo)
                        GeneratePropertyClone(generator, m as PropertyInfo);
                    else
                        GenerateFieldClone(generator, m as FieldInfo);
                }
            };

            // Do non-virtual first (always copy these)
            iter(
                members
                    .Where(m => !m.IsVirtual)
                    .Select(m => m.Member)
            );

            // Now generate check for the IsVirtual flag
            generator.Emit(OpCodes.Ldarg_2);
            generator.Emit(OpCodes.Brfalse, endOfFunction);

            // And output the virtual members copying logic
            iter(
                members
                    .Where(m => m.IsVirtual)
                    .Select(m => m.Member)
            );

            generator.MarkLabel(endOfFunction);

            generator.Emit(OpCodes.Ret);

            return dynMethod.CreateDelegate(typeof(CopyConstructorDelegate<T>)) as CopyConstructorDelegate<T>;
        }

        /// <summary>
        /// INPUTS: Expects source and destination to be in arguments 0 and 1 respectively.
        /// </summary>
        /// <param name="generator"></param>
        private static void GeneratePropertyClone(ILGenerator generator, PropertyInfo property)
        {
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Callvirt, property.GetGetMethod());
            generator.Emit(OpCodes.Callvirt, property.GetSetMethod());
        }

        /// <summary>
        /// INPUTS: Expects source and destination to be in arguments 0 and 1 respectively.
        /// </summary>
        /// <param name="generator"></param>
        private static void GenerateFieldClone(ILGenerator generator, FieldInfo field)
        {
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Ldfld, field);
            generator.Emit(OpCodes.Stfld, field);
        }
    }
}
