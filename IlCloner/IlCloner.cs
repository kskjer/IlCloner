using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Runtime.CompilerServices;
using System.Linq.Expressions;

namespace IlCloningGenerator
{
    public class IlCloner
    {
        public bool SaveAssemblies { get; set; } = false;
        public bool UseExistingCloners { get; set; } = false;
        public bool ShouldCloneProperties { get; set; } = true;
        public bool ShouldCloneFields { get; set; } = true;
        public Action<string> Logger { get; set; } = (s) => System.Diagnostics.Trace.WriteLine(s);

        private readonly Dictionary<Type, object> _customConstructors =
            new Dictionary<Type, object>();

        private readonly HashSet<MemberInfo> _excludedMembers = new HashSet<MemberInfo>();
        private readonly HashSet<MemberInfo> _includedMembers = new HashSet<MemberInfo>();
        private readonly List<Predicate<MemberInfo>> _straightCopyMemberWhen =
            new List<Predicate<MemberInfo>>()
            {
                (m) => m.GetCustomAttribute<XmlIgnoreAttribute>() != null
            };

        public void Exclude(PropertyInfo p) => _excludedMembers.Add(p);
        public void Exclude(FieldInfo f) => _excludedMembers.Add(f);
        public void Include(PropertyInfo p) => _includedMembers.Add(p);
        public void Include(FieldInfo f) => _includedMembers.Add(f);

        public void AddStraightCopyCondition(Predicate<MemberInfo> predicate) => _straightCopyMemberWhen.Add(predicate);
        public void AlwaysStraightCopy(PropertyInfo prop) => AddStraightCopyCondition(m => m is PropertyInfo && (m as PropertyInfo) == prop);
        public void AlwaysStraightCopy(FieldInfo field) => AddStraightCopyCondition(m => m is FieldInfo && (m as FieldInfo) == field);


        public void DefineCustomConstructor<T>(Func<T, T> fn)
        {
            lock (_lock)
                _customConstructors[typeof(T)] = fn;
        }


        private static readonly HashSet<Type> _toStraightCopy =
            new HashSet<Type>
            {
                typeof(string),
                typeof(decimal),
                typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan)
            };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldStraightCopy(Type t) =>
            _toStraightCopy.Contains(t) || t.IsPrimitive || t.IsEnum;

        private static bool IsValueType(Type t) =>
            t.IsValueType || t == typeof(string);


        public static IlCloner Default { get; } = new IlCloner();

        /// <param name="instance">This is just for type inference. Not actually used by the cloner function.</param>
        public static IIlClonerFluent<T> Fluent<T>(T instance = default(T)) =>
            new IlClonerFluent<T>();

        /// <param name="instance">This is just for type inference. Not actually used by the cloner function.</param>
        public static Func<T, T> CreateCloner<T>(T instance = default(T)) =>
            Default.CreateClonerDelegate(instance);

        /// <param name="instance">This is just for type inference. Not actually used by the cloner function.</param>
        public Func<T, T> CreateClonerDelegate<T>(T instance = default(T))
        {
            var t = typeof(T);

            lock (_lock)
            {
                if (!_generatedDelegates.ContainsKey(t))
                {
                    var minfo = CloneDeep(typeof(T));
                    var del = minfo.CreateDelegate(typeof(Func<T, T>)) as Func<T, T>;

                    _generatedDelegates.Add(t, del);

                    return del;
                }

                return _generatedDelegates[t] as Func<T, T>;
            }
        }

        private readonly object _lock = new object();

        private readonly Dictionary<Type, object> _generatedDelegates =
            new Dictionary<Type, object>();

        private readonly Dictionary<Type, MethodInfo> _generatedMethodInfo =
            new Dictionary<Type, MethodInfo>();

        private readonly Dictionary<Type, MethodInfo> _generatedOuterMethodInfo =
            new Dictionary<Type, MethodInfo>();

        private MethodInfo CloneDeep(Type t, State state = null, MemberInfo member = null)
        {
            lock (_lock)
                return InnerCloneDeep(t, state, member);
        }

        private MethodInfo InnerCloneDeep(Type t, State state = null, MemberInfo member = null)
        {
            var minfoSource = 
                (state?.Depth).GetValueOrDefault() == 0
                ? _generatedOuterMethodInfo 
                : _generatedMethodInfo;

            // If the type implements IList (non-generic collection), we have to generate unique code for 
            // every time we encounter it, since we don't know until runtime what the collection contains.
            if (minfoSource.ContainsKey(t) && CanCacheClonerForType(t))
                return minfoSource[t];

            // Set up our state (or update) that is passed down in recursive calls
            var stateName = $"CloneState_{t.Name}";

            if (state?.NeedReconstruction == true)
                state = new State(stateName, t, state);
            else
                state = state ?? new State(stateName, t);

            var dynMethod = state.TypeBuilder.DefineMethod(
                name          : $"DeepClone_{t.Name}", 
                attributes    : MethodAttributes.Public | MethodAttributes.Static,
                returnType    : t,
                parameterTypes: 
                    state.Depth == 0 || !CanBeInitializedBeforeCtor(t)
                    ? new[] { t }
                    : new[] { t, t }
            );

            dynMethod.SetCustomAttribute(
                new CustomAttributeBuilder(
                    typeof(MethodImplAttribute).GetConstructor(new[] { typeof(MethodImplOptions) }),
                    new object[] { MethodImplOptions.AggressiveInlining }
                )
            );

            var generator = dynMethod.GetILGenerator();

            state.Generator = state.Generator ?? generator;

            // Store a pointer to ourselves in case we need to clone recursively.
            minfoSource.Add(t, dynMethod);


            Logger($" {"-".PadLeft(state.Depth + 1)} {t.Name}");


            // If this is a key value pair, we have some special logic for it
            if (IsKeyValuePair(t))
            {
                GenerateKvpCloner(
                    state, 
                    t,
                    member,
                    generator,
                    g => g.Emit(OpCodes.Ldarga_S, 0)
                );

                goto completed;
            }

            // If this is a tuple, we have special logic for it
            if (IsTuple(t))
            {
                GenerateTupleCloner(
                    state,
                    t,
                    member,
                    generator,
                    g => g.Emit(OpCodes.Ldarg_0)
                );

                goto completed;
            }

            // If this is a value type or string, we just return it as is
            if (ShouldStraightCopy(t))
            {
                generator.Emit(OpCodes.Ldarg_0);

                goto completed;
            }

            var vNewObj = generator.DeclareLocal(t);

            if (t.IsValueType)
            {
                generator.Emit(OpCodes.Ldloca_S, vNewObj);
                generator.Emit(OpCodes.Initobj, t);
            }
            else if (t.IsArray)
            {
                generator.Emit(OpCodes.Ldarg_0);
                generator.Emit(OpCodes.Stloc, vNewObj);

                GenerateArrayCloning(
                    state,
                    vNewObj,
                    t,
                    null
                );

                generator.Emit(OpCodes.Ldloc, vNewObj);

                goto completed;
            }
            else
            {
                GenerateObjectConstruction(t, state, generator);

                generator.Emit(OpCodes.Stloc, vNewObj);
            }

            //
            // Build a list of the fields and the properties to create cloning IL for
            //
            var fields = t
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => ShouldCloneFields)
                .Where(f => !_excludedMembers.Contains(f) && (_includedMembers.Contains(f) || f.GetCustomAttribute<XmlIgnoreAttribute>() == null))
                .Where(p => !p.IsInitOnly) // We don't want readonly fields
                .Select(f => new
                {
                    field = f,
                    prop = null as PropertyInfo,
                    local = generator.DeclareLocal(f.FieldType)
                });

            var members = t
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => ShouldCloneProperties)
                .Where(p => !_excludedMembers.Contains(p) && (_includedMembers.Contains(p) || p.GetCustomAttribute<XmlIgnoreAttribute>() == null))
                .Where(p => p.GetGetMethod() != null && p.GetSetMethod() != null)
                // We don't want indexers
                .Where(p => p.GetIndexParameters().Length == 0)
                .Select(p => new
                {
                    field = null as FieldInfo,
                    prop = p,
                    local = generator.DeclareLocal(p.PropertyType)
                })
                .Concat(fields)
                .ToArray();
            
            // We use similar code for both properties and fields since they're just class members
            foreach (var f in members)
            {
                if (t.IsValueType)
                    generator.Emit(OpCodes.Ldarga_S, 0);
                else
                    generator.Emit(OpCodes.Ldarg_0);

                if (f.field != null)
                    GenerateFieldClone(state, vNewObj, f.field);
                else
                    GeneratePropertyClone(state, vNewObj, f.prop);
            }


            //
            // Generate IL to clone contents of collections
            //
            GenerateCollectionCloning(state, vNewObj, t, member);



            generator.Emit(OpCodes.Ldloc, vNewObj);

        completed:

            generator.Emit(OpCodes.Ret);

            // If we're at the root of the clone method generation, we need to assemble the dynamic
            // type and then replace the MethodInfos that are MethodBuilders with REAL MethodInfos 
            // from the created type.
            if (state.Depth == 0)
            { 
                var allMethods = state.FinalizeType(this)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static);

                foreach (var m in allMethods)
                {
                    if (m.GetParameters().Length == 1)
                        _generatedOuterMethodInfo[m.ReturnType] = m;
                    else
                        _generatedMethodInfo[m.ReturnType] = m;
                }
            }

            return minfoSource[t];
        }

        private void GenerateObjectConstruction(Type t, State state, ILGenerator generator)
        {
            var ctor = t.GetConstructor(new Type[0]);

            if (_customConstructors.ContainsKey(t))
                GenerateCallToCustomConstructor(t, state, generator);
            else if (ctor == null)
                throw new ConstructorNotFoundException($"Constructor not found for type '{t.Name}'", t);
            else
            {
                // If we're not the root cloning method, and if this is not a value type,
                // we will be supplied with the value of the field as the second argument
                // to the method.
                // Some classes specify field initializers for collection, so instead of
                // doubly instantiating them, we'll check if the value is usable! (non-null)
                if (state.Depth > 0 && CanBeInitializedBeforeCtor(t))
                {
                    var labelAfterConstruct = generator.DefineLabel();

                    generator.Emit(OpCodes.Ldarg_1);
                    generator.Emit(OpCodes.Dup);
                    generator.Emit(OpCodes.Brtrue, labelAfterConstruct);
                    generator.Emit(OpCodes.Pop);

                    generator.Emit(OpCodes.Newobj, ctor);

                    generator.MarkLabel(labelAfterConstruct);
                }
                // If object to be cloned is a dictionary, we call the constructor that accepts a
                // capacity as an integer. This speeds up the process considerably.
                else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {

                    var dctor = t.GetConstructor(new[] { typeof(int) });
                    var countProp = t.GetProperty("Count");
                    
                    generator.Emit(OpCodes.Ldarg_0);
                    generator.Emit(OpCodes.Callvirt, countProp.GetGetMethod());
                    generator.Emit(OpCodes.Newobj, dctor);
                }
                else
                {
                    generator.Emit(OpCodes.Newobj, ctor);
                }
            }
        }

        private void GenerateCallToCustomConstructor(Type t, State state, ILGenerator generator)
        {
            generator.Emit(OpCodes.Ldsfld, state.GetFieldForCustomConstructor(t));
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Callvirt, typeof(Func<,>).MakeGenericType(t, t).GetMethod("Invoke"));
        }

        private static bool CanCacheClonerForType(Type t) => 
            !typeof(IList).IsAssignableFrom(t);


        /// <summary>
        /// Generate a method with IL that fits the prototype <see cref="Action{T1, T2}"/>.
        /// </summary>
        private Action<IList, IList> CreateNonGenericListFillerDelegate(State state, Type listType, Type elementType, MemberInfo member)
        {
            var rval = CreateNonGenericListFiller(state, listType, elementType, member)
                .CreateDelegate(typeof(Action<IList, IList>)) as Action<IList, IList>;

            lock (_nongenericListFillers)
                _nongenericListFillers.Add(rval);

            return rval;
        }

        private static readonly List<object> _nongenericListFillers =
            new List<object>();
            

        /// <summary>
        /// Generate a method with IL that fits the prototype <see cref="Action{T1, T2}"/>.
        /// </summary>
        private MethodInfo CreateNonGenericListFiller(State state, Type listType, Type elementType, MemberInfo member)
        {
            var stateName = $"NonGenericListFill_{listType.Name}_{elementType.Name}";

            if (state?.NeedReconstruction == true)
                state = new State(stateName, listType, state);
            else
                throw new ArgumentException("State should always need reconstruction at this point.");

            var dynMethod = state.TypeBuilder.DefineMethod(
                name: $"{stateName}",
                attributes: MethodAttributes.Public | MethodAttributes.Static,
                returnType: typeof(void),
                parameterTypes: new[] { typeof(IList), typeof(IList) }
            );

            state.Generator = dynMethod.GetILGenerator();

            Logger($" [Runtime IList] {"-".PadLeft(state.Depth + 1)} {listType.Name}");

            // 
            // Pop second arg into the newVar variable slot, since this is the newly cloned list
            //
            var newVar = state.Generator.DeclareLocal(elementType);
            state.Generator.Emit(OpCodes.Ldarg_1);
            state.Generator.Emit(OpCodes.Stloc, newVar);
            
            GenerateIListCloning(state, newVar, listType, member);

            state.Generator.Emit(OpCodes.Ret);

            return state.FinalizeType(this).GetMethod(dynMethod.Name);
        }


        /// <summary>
        /// Generate IL for deep cloning <see cref="KeyValuePair{TKey, TValue}"/> structs.<para/>
        /// OUTPUTS: New KVP is on the stack.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="state"></param>
        /// <param name="generator"></param>
        /// <param name="loadAddrGen">Generate IL the loads the KVP onto the evaluation stack.</param>
        private void GenerateKvpCloner(State state, Type kvpType, MemberInfo member, ILGenerator generator, Action<ILGenerator> loadAddrGen)
        {
            var kvpTypes = GetKeyValuePairTypes(kvpType);
            var keyType = kvpTypes[0];
            var valueType = kvpTypes[1];

            var keyGetter = kvpType.GetProperty("Key").GetGetMethod();
            var valueGetter = kvpType.GetProperty("Value").GetGetMethod();
            var ctor = kvpType.GetConstructor(new[] { keyType, valueType });

            loadAddrGen(generator);
            generator.Emit(OpCodes.Call, keyGetter);
            loadAddrGen(generator);
            generator.Emit(OpCodes.Call, valueGetter);

            // Should we perform a deep clone of the value?
            if (!ShouldStraightCopy(valueType))
                GenerateClonerThunk(state, null, valueType, generator);

            generator.Emit(OpCodes.Newobj, ctor);
        }

        /// <summary>
        /// Generate IL for deep cloning Tuple types<para/>
        /// OUTPUTS: New Tuple on the evaluation stack.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="state"></param>
        /// <param name="generator"></param>
        /// <param name="loadAddrGen">Generate IL the loads the KVP onto the evaluation stack.</param>
        private void GenerateTupleCloner(State state, Type tupleType, MemberInfo member, ILGenerator generator, Action<ILGenerator> loadAddrGen)
        {
            var propsToLoad = tupleType
                .GenericTypeArguments
                .Select((t, idx) => tupleType.GetProperty($"Item{idx + 1}"));

            var ctor = typeof(Tuple)
                .GetMethods()
                .Where(m => m.Name == "Create" && m.ContainsGenericParameters)
                .Where(m => m.GetParameters().Length == tupleType.GenericTypeArguments.Length)
                .First()
                .MakeGenericMethod(tupleType.GenericTypeArguments);


            foreach (var prop in propsToLoad)
            {
                loadAddrGen(generator);
                generator.Emit(OpCodes.Callvirt, prop.GetGetMethod());

                if (!ShouldStraightCopy(prop.PropertyType))
                    GenerateClonerThunk(state, null, prop.PropertyType, generator);
            }

            generator.Emit(OpCodes.Call, ctor);
        }


        /// <summary>
        /// EXPECTS: Object instance to be on the stack.<para/>
        /// OUTPUTS: None.<para/>
        /// Generates IL that loads the specified member (field or property) from the object 
        /// currently on the stack.<para/>
        /// Generators for IL that loads and stores to the field or property must be supplied by
        /// the caller.<para/>
        /// It then generates the recursive cloning logic and stores the result in the supplied
        /// new object.
        /// </summary>
        /// <param name="generator"></param>
        /// <param name="vNewObj"></param>
        /// <param name="field"></param>
        /// <param name="local"></param>
        private void GenerateMemberClone(
            State state, LocalBuilder vNewObj, MemberInfo member, Type memberType,
            Action<ILGenerator> generateLoad,
            Action<ILGenerator> generateStore)
        {
            var generator = state.Generator;
            var shouldStraightCopy = ShouldStraightCopy(memberType) || _straightCopyMemberWhen.Any(p => p(member));
            var isValueType = IsValueType(memberType);

            var endOfCurrent = generator.DefineLabel();
            var local = generator.DeclareLocal(memberType);

            //generator.Emit(OpCodes.Ldfld, member);
            generateLoad(generator);

            if (!shouldStraightCopy && !isValueType)
                generator.Emit(OpCodes.Dup);

            generator.Emit(OpCodes.Stloc, local);

            if (!shouldStraightCopy || memberType.IsArray)
            {
                if (!isValueType)
                    generator.Emit(OpCodes.Brfalse_S, endOfCurrent);

                // Special treatment for arrays!
                if (memberType.IsArray)
                {
                    GenerateArrayCloning(state, local, memberType, member);
                }
                else
                {
                    generator.Emit(OpCodes.Ldloc, local);
                    GenerateClonerThunk(state, member, memberType, generator, vNewObj);
                    generator.Emit(OpCodes.Stloc, local);
                }
            }

            GenerateLocalLoadInstance(vNewObj, generator);
            generator.Emit(OpCodes.Ldloc, local);
            generateStore(generator);

            generator.MarkLabel(endOfCurrent);
        }

        private static void GenerateLocalLoadInstance(LocalBuilder vNewObj, ILGenerator generator)
        {
            generator.Emit(vNewObj.LocalType.IsValueType ? OpCodes.Ldloca_S : OpCodes.Ldloc, vNewObj);
        }

        private void GeneratePropertyClone(State state, LocalBuilder vNewObj, PropertyInfo info)
        {
            var getMethod = info.GetGetMethod();
            var setMethod = info.GetSetMethod();

            GenerateMemberClone(
                state,
                vNewObj,
                info,
                info.PropertyType,
                g => GeneratePropertyAccess(info, getMethod, g),
                g => GeneratePropertyAccess(info, setMethod, g)
            );
        }

        /// <summary>
        /// EXPECTS: Object instance to be on the stack.<para/>
        /// OUTPUTS: None.<para/>
        /// Generates IL that loads the specified field from the object currently on the stack.
        /// It then generates the recursive cloning logic and stores the result in the supplied
        /// new object.
        /// </summary>
        /// <param name="generator"></param>
        /// <param name="vNewObj"></param>
        /// <param name="field"></param>
        /// <param name="local"></param>
        private void GenerateFieldClone(State state, LocalBuilder vNewObj, FieldInfo field) =>
            GenerateMemberClone(
                state,
                vNewObj,
                field,
                field.FieldType,
                g => g.Emit(OpCodes.Ldfld, field),
                g => g.Emit(OpCodes.Stfld, field)
            );

        private MethodInfo GetMethodInfoForCloner(State state, Type type, MemberInfo member)
        {
            if (_generatedMethodInfo.ContainsKey(type))
                return _generatedMethodInfo[type];

            return CloneDeep(type, state.Descend(), member);
        }




        /// <summary>
        /// EXPECTS: <para/>
        /// OUTPUTS: None.<para/>
        /// Generates IL for cloning arrays.
        /// </summary>
        /// <remarks>
        /// 
        /// Should we move this into <see cref="InnerCloneDeep{T}(State)"/> so that an array
        /// can be passed as an argument? This would be more in line with how it works otherwise.
        /// 
        /// </remarks>
        /// <param name="local">Variable containing the value of the array to be cloned here.</param>
        /// <param name="generator"></param>
        /// <param name="type"></param>
        private void GenerateArrayCloning(State state, LocalBuilder local, Type type, MemberInfo member)
        {
            var elementType = type.GetElementType();
            var generator = state.Generator;

            var outputVar = generator.DeclareLocal(type);
            var arrayLengthVar = generator.DeclareLocal(typeof(int));
            var iteratorVar = generator.DeclareLocal(typeof(int));

            var loopCheckCondLabel = generator.DefineLabel();
            var loopStartLabel = generator.DefineLabel();

            generator.Emit(OpCodes.Ldloc, local);
            generator.Emit(OpCodes.Ldlen);
            generator.Emit(OpCodes.Conv_I4);
            generator.Emit(OpCodes.Dup);
            generator.Emit(OpCodes.Stloc, arrayLengthVar); // load, dup, and preserve array length

            generator.Emit(OpCodes.Newarr, elementType);
            generator.Emit(OpCodes.Stloc, outputVar);
                
            generator.Emit(OpCodes.Ldc_I4_0);
            generator.Emit(OpCodes.Stloc, iteratorVar);

            generator.Emit(OpCodes.Br_S, loopCheckCondLabel);

            // loop body
            {
                generator.MarkLabel(loopStartLabel);

                // Copy from one array to the other
                generator.Emit(OpCodes.Ldloc, outputVar);
                generator.Emit(OpCodes.Ldloc, iteratorVar);
                generator.Emit(OpCodes.Ldloc, local);
                generator.Emit(OpCodes.Ldloc, iteratorVar);
                generator.Emit(OpCodes.Ldelem, elementType); // element loaded here

                // Now, should we clone? Or recurse?
                if (elementType.IsArray)
                {
                    // Create a new variable for our nested call to output to
                    var nestedArrayVar = generator.DeclareLocal(elementType);

                    generator.Emit(OpCodes.Stloc, nestedArrayVar);
                    GenerateArrayCloning(state, nestedArrayVar, elementType, member);
                    generator.Emit(OpCodes.Ldloc, nestedArrayVar);
                }
                else if (!ShouldStraightCopy(elementType))
                {
                    GenerateClonerThunk(state, member, elementType, generator);
                }

                generator.Emit(OpCodes.Stelem, elementType); // element stored here

                // Update counter
                generator.Emit(OpCodes.Ldloc, iteratorVar);
                generator.Emit(OpCodes.Ldc_I4_1);
                generator.Emit(OpCodes.Add);
                generator.Emit(OpCodes.Stloc, iteratorVar);

                // Here the loop checks the conditions for continuation
                generator.MarkLabel(loopCheckCondLabel);

                generator.Emit(OpCodes.Ldloc, iteratorVar);
                generator.Emit(OpCodes.Ldloc, arrayLengthVar);
                generator.Emit(OpCodes.Blt_S, loopStartLabel);
            }

            generator.Emit(OpCodes.Ldloc, outputVar);
            generator.Emit(OpCodes.Stloc, local);
        }

        /// <summary>
        /// EXPECTS: The original collection instance as argument 0<para/>
        /// OUTPUTS: None.<para/>
        /// Generates calls to the generic methods which performs additional copying on collections.
        /// </summary>
        /// <param name="generator"></param>
        /// <param name="type"></param>
        private void GenerateCollectionCloning(State state, LocalBuilder newVar, Type type, MemberInfo member)
        {
            foreach (var iface in type.GetInterfaces())
            {
                if (iface == typeof(IList))
                {
                    GenerateIListCloning(state, newVar, type, member);
                    return;
                }

                if (iface == typeof(IDictionary))
                {
                    GenerateICollectionCloning(
                        state,
                        newVar,
                        type,
                        member,
                        typeof(DictionaryEntry),
                        typeof(IEnumerable),
                        typeof(IEnumerator),
                        typeof(IDictionary)
                    );
                    return;
                }

                if (!iface.IsGenericType)
                    continue;

                if (iface.GetGenericTypeDefinition() == typeof(ICollection<>))
                {
                    GenerateICollectionCloning(
                        state,
                        newVar,
                        type,
                        member,
                        iface.GenericTypeArguments[0],
                        typeof(IEnumerable<>).MakeGenericType(iface.GenericTypeArguments),
                        typeof(IEnumerator<>).MakeGenericType(iface.GenericTypeArguments),
                        typeof(ICollection<>).MakeGenericType(iface.GenericTypeArguments)
                    );
                    return;
                }
            }
        }

        private static void IListCloneInternal<T>(IList input, IList output, Func<T, T> cloner)
        {
            foreach (var i in input)
                output.Add(cloner((T)i));
        }

        private void GenerateIListCloning(State state, LocalBuilder newVar, Type type, MemberInfo member)
        {
            var enumerable = type
                .GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .SingleOrDefault();

            Type elementType = enumerable?.GenericTypeArguments?[0];

            // If this is an IList that implements IEnumerable<T> then we can create static 
            // cloning code for it, since we know the element type.
            if (enumerable != null || (elementType = state.ElementTypeGetter(type, member)) != null)
            {
                // Only if we can instantiate this type do we generate concrete code.
                // If it's an abstract class or an interface, we fall through to the
                // below case which generates cloning code at runtime when we know 
                // what time is actually here.
                //
                // TODO: This is probably a very poor solution. The reason for having 
                // abstract or interface types in a collection is for the sake of 
                // polymorphism, so whatever the first type in the collection at runtime
                // may not be representative.
                if (!elementType.IsAbstract && !elementType.IsInterface)
                {
                    GenerateICollectionCloning(
                        state,
                        newVar,
                        type,
                        member,
                        elementType,
                        typeof(IEnumerable),
                        typeof(IEnumerator),
                        typeof(IList)
                    );

                    return;
                }
            }

            var generator = state.Generator;
            var listType = (member as FieldInfo)?.FieldType ??
                (member as PropertyInfo)?.PropertyType ??
                type;
            var listClonerType = typeof(RuntimeIListCloner<>).MakeGenericType(
                (member as FieldInfo)?.FieldType ??
                (member as PropertyInfo)?.PropertyType ??
                listType
            );

            var ourClonerField = state.TypeBuilder.DefineField(
                $"_cloner_IList_{member?.Name ?? type.Name}",
                listClonerType,
                FieldAttributes.Private | FieldAttributes.Static
            );

            //
            // Since we have to go through the whole message again with these non-generic lists,
            // the instance of the IList cloner needs to know for which field, property, or 
            // standalone type it was created for. This runtime type handle is then placed into
            // the state object for the clone generator, and used during collection cloning 
            // generation to create a concrete cloner.
            //
            if (member is FieldInfo)
            {
                state.StaticConstructor.Emit(OpCodes.Ldtoken, member as FieldInfo);
                state.StaticConstructor.Emit(OpCodes.Call, typeof(FieldInfo).GetMethod(nameof(FieldInfo.GetFieldFromHandle), new[] { typeof(RuntimeFieldHandle) }));
            }
            else if (member is PropertyInfo)
            {
                state.StaticConstructor.Emit(OpCodes.Ldtoken, (member as PropertyInfo).GetGetMethod());
                state.StaticConstructor.Emit(OpCodes.Call, typeof(MethodBase).GetMethod(nameof(MethodBase.GetMethodFromHandle), new[] { typeof(RuntimeMethodHandle) }));
            }
            else
            {
                state.StaticConstructor.Emit(OpCodes.Ldtoken, type);
                state.StaticConstructor.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), BindingFlags.Static | BindingFlags.Public));
            }

            state.StaticConstructor.Emit(OpCodes.Ldsfld, state.ClonerInstanceField);
            state.StaticConstructor.Emit(OpCodes.Newobj, listClonerType.GetConstructor(new[] { typeof(object), typeof(IlCloner) }));
            state.StaticConstructor.Emit(OpCodes.Stsfld, ourClonerField);

            // Now in our cloning method we generate a call to the stateful RuntimeIlistCloner
            generator.Emit(OpCodes.Ldsfld, ourClonerField);
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldloc, newVar);
            generator.Emit(OpCodes.Call, listClonerType.GetMethod(nameof(RuntimeIListCloner<CollectionBase>.CopyList)));
        }

        private static int _dynTypeIdx = -1;

        private static TypeBuilder CreateTypeBuilder(string genTypeName, out AssemblyBuilder asmBuilder, out FieldInfo clonerInstanceField)
        {
            genTypeName = string.Format("DynGen{0:d4}_{1}", Interlocked.Increment(ref _dynTypeIdx), genTypeName);

            var asmName = new AssemblyName(genTypeName);

            asmBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.RunAndSave, AppDomain.CurrentDomain.BaseDirectory);
            var moduleBuilder = asmBuilder.DefineDynamicModule(genTypeName, $"{genTypeName}.dll");
            var typeBuilder = moduleBuilder.DefineType(
                genTypeName,
                TypeAttributes.Public |
                TypeAttributes.Class |
                TypeAttributes.AutoClass |
                TypeAttributes.AnsiClass |
                TypeAttributes.AutoLayout,
                typeof(object)
            );

            // We can't use the above type for this, since we're using its static constructor
            // to initialize some stuff. The clone generation routine has to set a static field
            // in a class to its own instance before any such constructor is run.
            var forClonerInstance = moduleBuilder.DefineType(
                $"{genTypeName}_static",
                TypeAttributes.Public |
                TypeAttributes.Class |
                TypeAttributes.AutoClass |
                TypeAttributes.AnsiClass |
                TypeAttributes.AutoLayout,
                typeof(object)
            );


            clonerInstanceField = forClonerInstance.DefineField(
                "_ilClonerInstance",
                typeof(IlCloner),
                FieldAttributes.Public | FieldAttributes.Static
            );

            var t = forClonerInstance.CreateType();

            clonerInstanceField = t.GetField(clonerInstanceField.Name);

            return typeBuilder;
        }

        /// <summary>
        /// EXPECTS: Argument 0 to be the input list. 
        /// OUTPUTS: None.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="newVar"></param>
        /// <param name="fieldType">
        /// The actual type of the object containing a list.
        /// </param>
        /// <param name="member"></param>
        /// <param name="elementType">
        /// The type of the elements in the list.
        /// </param>
        /// <param name="listType">
        /// The type used for enumerating through the list. Usually IEnumerable&lt;T&gt; or IEnumerable
        /// </param>
        /// <param name="enumType">
        /// The type of the enumerator to generate code for.
        /// </param>
        /// <param name="collectionType">
        /// The type of the underlying collection to generate code for. This is where we fetch the "Add" method from.
        /// </param>
        /// <returns></returns>
        private void GenerateICollectionCloning(State state, LocalBuilder newVar, Type fieldType, MemberInfo member, Type elementType, Type listType, Type enumType, Type collectionType)
        {
            var generator = state.Generator;
            var afterClear = generator.DefineLabel();

            var clearMethod = collectionType.GetMethod("Clear");
            var countProp = collectionType.GetProperty("Count")?.GetGetMethod();

            //
            // If the collection is initialized at the field and contents are added,
            // we need to clear its contents before adding what is in the new object.
            //
            // TODO: Investigate if this significantly slows down the cloning and if
            //       we should add this check somewhere else.
            //
            if (clearMethod != null && countProp != null)
            {
                generator.Emit(OpCodes.Ldloc, newVar);
                generator.Emit(OpCodes.Callvirt, countProp);

                generator.Emit(OpCodes.Brfalse_S, afterClear);

                generator.Emit(OpCodes.Ldloc, newVar);
                generator.Emit(OpCodes.Callvirt, collectionType.GetMethod("Clear"));

                generator.MarkLabel(afterClear);
            }

            if (collectionType == typeof(IList) ||
                fieldType.IsGenericType && typeof(IList<>).MakeGenericType(fieldType.GenericTypeArguments[0]).IsAssignableFrom(fieldType))
            {
                GenerateICollectionIndexerCloning(
                    state,
                    newVar,
                    fieldType,
                    member,
                    elementType,
                    listType,
                    enumType,
                    collectionType
                );

                return;
            }

            var isDisposable = typeof(IDisposable).IsAssignableFrom(enumType);

            var getEnumMethod = listType
                .GetMethod("GetEnumerator", BindingFlags.Public | BindingFlags.Instance);
            var getCurrentProp = enumType.GetProperty("Current").GetGetMethod();
            var enumMoveNext = typeof(IEnumerator)
                .GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance);
            var addMethod = collectionType
                .GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
            var disposeMethod = typeof(IDisposable)
                .GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance);

            var enumVar = generator.DeclareLocal(enumType);
            var currentVar = generator.DeclareLocal(elementType);

            // Call GetEnumerator()
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Callvirt, getEnumMethod);
            generator.Emit(OpCodes.Stloc, enumVar);

            // Here we loop through the items
            var labelLoopStart = generator.DefineLabel();
            var labelMoveNext = generator.DefineLabel();
            var labelEnd = generator.DefineLabel();

            if (isDisposable)
                generator.BeginExceptionBlock();
            {
                generator.Emit(OpCodes.Br_S, labelMoveNext);   // Loop start

                generator.MarkLabel(labelLoopStart);
                generator.Emit(OpCodes.Ldloc, enumVar);
                generator.Emit(OpCodes.Callvirt, getCurrentProp);

                if (elementType.IsValueType && !getCurrentProp.ReturnType.IsValueType)
                    generator.Emit(OpCodes.Unbox_Any, elementType);

                generator.Emit(OpCodes.Stloc, currentVar);

                generator.Emit(OpCodes.Ldloc, newVar);

                if (ShouldDeepCloneElementOfType(elementType))
                {
                    generator.Emit(OpCodes.Ldloc, currentVar);
                    GenerateClonerThunk(state, member, elementType, generator);
                }
                else
                {
                    generator.Emit(OpCodes.Ldloc, currentVar);
                }

                // Now we have the newly created KVP on the evaluation stack
                // Unfortunately, the IDictionary type does not have an Add() 
                // method which accepts a KVP. 
                //
                // For this specific instance, we have to deconstruct the type
                // we just created.
                //
                // There is improvement potential here as the destructuring 
                // is redundant, but it keeps the code simpler.
                if (collectionType == typeof(IDictionary))
                {
                    var dictVar = generator.DeclareLocal(elementType);

                    generator.Emit(OpCodes.Stloc, dictVar);
                    generator.Emit(OpCodes.Ldloca, dictVar);
                    generator.Emit(OpCodes.Call, elementType.GetProperty("Key").GetGetMethod());
                    generator.Emit(OpCodes.Ldloca, dictVar);
                    generator.Emit(OpCodes.Call, elementType.GetProperty("Value").GetGetMethod());
                }

                generator.Emit(OpCodes.Callvirt, addMethod);

                // Discard the return value for IList.Add()
                if (addMethod.ReturnType != typeof(void))
                    generator.Emit(OpCodes.Pop);

                generator.MarkLabel(labelMoveNext);
                generator.Emit(OpCodes.Ldloc, enumVar);
                generator.Emit(OpCodes.Callvirt, enumMoveNext);

                generator.Emit(OpCodes.Brtrue_S, labelLoopStart);
            }


            if (isDisposable)
            {
                generator.BeginFinallyBlock();
                var labelEndFinally = generator.DefineLabel();

                generator.Emit(OpCodes.Ldloc, enumVar);
                generator.Emit(OpCodes.Brfalse_S, labelEndFinally);

                generator.Emit(OpCodes.Ldloc, enumVar);
                generator.Emit(OpCodes.Callvirt, disposeMethod);

                generator.MarkLabel(labelEndFinally);
                generator.EndExceptionBlock();
            }

            generator.MarkLabel(labelEnd);
        }

        private void GenerateICollectionIndexerCloning(State state, LocalBuilder newVar, Type fieldType, MemberInfo member, Type elementType, Type listType, Type enumType, Type collectionType)
        {
            var generator = state.Generator;
            var addMethod = collectionType.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
            var countProp = fieldType.GetProperty("Count").GetGetMethod();
            var getIndexMethod = fieldType.GetProperties()
                .Concat(collectionType.GetProperties())
                .Where(p => p.GetIndexParameters()?.Length == 1)
                .FirstOrDefault()
                .GetGetMethod();

            var countVar = generator.DeclareLocal(countProp.ReturnType);
            var idxVar = generator.DeclareLocal(typeof(int));

            var loopEpilogue = generator.DefineLabel();
            var loopStart = generator.DefineLabel();

            // Fetch and store count of list
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Call, countProp);
            generator.Emit(OpCodes.Stloc, countVar);

            // for (idx = 0; idx < count; i++) ...
            generator.Emit(OpCodes.Ldc_I4_0);
            generator.Emit(OpCodes.Stloc, idxVar);

            generator.Emit(OpCodes.Br_S, loopEpilogue);

            // -- inner loop

            generator.MarkLabel(loopStart);

            generator.Emit(OpCodes.Ldloc, newVar); // we push the new collection instance back here, so when the below
                                                    // is done we can just call the add method without having to swap
                                                    // values.

            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldloc, idxVar);
            generator.Emit(OpCodes.Callvirt, getIndexMethod);

            if (ShouldDeepCloneElementOfType(elementType))
                GenerateClonerThunk(state, member, elementType, generator);

            generator.Emit(OpCodes.Callvirt, addMethod);

            // Discard the return value for IList.Add()
            if (addMethod.ReturnType != typeof(void))
                generator.Emit(OpCodes.Pop);

            // Increment counter
            generator.Emit(OpCodes.Ldloc, idxVar);
            generator.Emit(OpCodes.Ldc_I4_1);
            generator.Emit(OpCodes.Add);
            generator.Emit(OpCodes.Stloc, idxVar);

            // Check condition here
            generator.MarkLabel(loopEpilogue);
            generator.Emit(OpCodes.Ldloc, idxVar);
            generator.Emit(OpCodes.Ldloc, countVar);
            generator.Emit(OpCodes.Blt_S, loopStart);
        }

        /// <summary>
        /// INPUTS: Expects the current item being cloned to be on the evaluation stack</para>
        /// OUTPUTS: The newly cloned object at the top of the evaluation stack.</para>
        /// Generates the IL that will call the cloner method. This method may inline some types being cloned.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="field"></param>
        /// <param name="generator"></param>
        private void GenerateClonerThunk(State state, MemberInfo member, Type type, ILGenerator generator, LocalBuilder newObj = null)
        {
            if (!IsKeyValuePair(type) && !IsTuple(type))
            {
                MethodInfo minfoCloner;

                if (type == typeof(object) || type.IsInterface || type.IsAbstract)
                {
                    var objInstance = generator.DeclareLocal(type);

                    generator.Emit(OpCodes.Stloc, objInstance);
                    generator.Emit(OpCodes.Ldsfld, state.ClonerInstanceField);
                    generator.Emit(OpCodes.Ldloc, objInstance);
                    generator.Emit(OpCodes.Call,
                        typeof(IlCloner).GetMethod(nameof(RuntimeClone), BindingFlags.Public | BindingFlags.Instance));

                    return;
                }

                // Let's see if there's an existing cloner if we need to
                if (UseExistingCloners && (minfoCloner = type.GetMethod("Clone", BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public, Type.DefaultBinder, new Type[0], null)) != null)
                    GenerateCallToNativeCloner(type, generator, minfoCloner);
                else
                {
                    // Here we generate a load of the new'd obj's value of the field or
                    // property. Some classes choose to initialize empty collections 
                    // directly on the member definition, instead of in the constructor.
                    // By checking this value and passing it to the child constructor,
                    // we can avoid duplicating the object instantiation.
                    if (CanBeInitializedBeforeCtor(type))
                    {
                        if (newObj != null)
                        {
                            GenerateLocalLoadInstance(newObj, generator);

                            if (member is PropertyInfo)
                                GeneratePropertyAccess(member, (member as PropertyInfo).GetGetMethod(), generator);
                            else
                                generator.Emit(OpCodes.Ldfld, member as FieldInfo);
                        }
                        else
                        {
                            generator.Emit(OpCodes.Ldnull);
                        }
                    }
                    
                    generator.Emit(OpCodes.Call, GetMethodInfoForCloner(state, type, member));
                }

                return;
            }

            var localVar = generator.DeclareLocal(type);

            generator.Emit(OpCodes.Stloc, localVar);

            if (IsKeyValuePair(type))
            {
                // Let's do some KVP magic
                GenerateKvpCloner(
                    state,
                    type,
                    member,
                    generator,
                    g => g.Emit(OpCodes.Ldloca_S, localVar)
                );
            }
            else if (IsTuple(type))
            {
                GenerateTupleCloner(
                    state,
                    type,
                    member,
                    generator,
                    g => g.Emit(OpCodes.Ldloc, localVar)
                );
            }
        }

        private static void GeneratePropertyAccess(MemberInfo member, MethodInfo getOrSetMethod, ILGenerator generator)
        {
            generator.Emit(
                member.DeclaringType.IsValueType 
                ? OpCodes.Call 
                : OpCodes.Callvirt,
                getOrSetMethod
            );
        }

        private readonly Dictionary<Type, Func<object, object>> _runtimeCloners =
            new Dictionary<Type, Func<object, object>>();



        /// <remarks>
        /// This method is used for when there is absolutely no way we can detect
        /// what type is going to be stored in a field.
        /// 
        /// It works by checking the runtime type of an object using GetType() and
        /// generating a cloner based on this. The cloner delegates that it generates
        /// are cached and stored in a dictionary for reuse (<see cref="_runtimeCloners"/>).
        /// </remarks>
        /// <summary>
        /// This method is for use by generated code ONLY. Use <see cref="CreateClonerDelegate{T}(T)"/>
        /// to create a cloner method that you can reuse.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        [Obsolete("This method should not be called directly.")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public object RuntimeClone(object input)
        {
            if (input == null)
                return null;

            var type = input.GetType();

            if (ShouldStraightCopy(type))
                return input;

            Func<object, object> wrapper;

            lock (_lock)
            {
                if (_runtimeCloners.ContainsKey(type))
                    return _runtimeCloners[type](input);

                Logger($"[Runtime Clone] {type.Name}");

                var realCloner = typeof(IlCloner)
                    .GetMethod(nameof(CreateClonerDelegate))
                    .MakeGenericMethod(type)
                    .Invoke(this, new object[] { null });

                var inputParam = Expression.Parameter(typeof(object));

                wrapper = 
                    Expression.Lambda<Func<object, object>>(
                        Expression.Invoke(
                            Expression.Constant(realCloner),
                            Expression.Convert(
                                inputParam,
                                type
                            )
                        ),
                        inputParam
                    )
                    .Compile();

                _runtimeCloners.Add(type, wrapper);
            }
            
            return wrapper(input);
        }

        private static bool IsKeyValuePair(Type type)
        {
            return 
                type == typeof(DictionaryEntry) || 
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
        }

        private static bool IsTuple(Type type)
        {
            return type.FullName.StartsWith("System.Tuple`");
        }

        private static Type[] GetKeyValuePairTypes(Type type) =>
            type == typeof(DictionaryEntry)
            ? new[] { typeof(object), typeof(object) }
            : type.GenericTypeArguments;

        private static bool CanBeInitializedBeforeCtor(Type type)
        {
            return !type.IsValueType;
        }

        private static void GenerateCallToNativeCloner(Type type, ILGenerator generator, MethodInfo cloner)
        {
            var local = generator.DeclareLocal(type);

            if (type.IsValueType)
            {
                generator.Emit(OpCodes.Stloc, local);
                generator.Emit(OpCodes.Ldloca, local);
            }

            generator.Emit(OpCodes.Callvirt, cloner);
        }

        private static bool ShouldDeepCloneElementOfType(Type element)
        {
            return (
                !element.IsValueType && element != typeof(string)
            ) || IsKeyValuePair(element);
        }


        //
        // Nested classes
        //
        #region Nested classes
        public class RuntimeIListCloner<TList>
            where TList : IList
        {
            /// <summary>
            /// Supplied by the static constructor of the cloner class. 
            /// See <see cref="GenerateIListCloning(State, LocalBuilder, Type, MemberInfo)"/>
            /// </summary>
            private readonly IlCloner _clonerInstance;
            private readonly object _lock = new object();
            private readonly object _key;
            private Action<IList, IList> _copier;

            public void CopyList(TList input, TList output)
            {
                if (_copier != null)
                {
                    _copier(input, output);
                    return;
                }

                lock (_lock)
                {
                    var enumerator = input.GetEnumerator();

                    if (!enumerator.MoveNext())
                        return;

                    var firstItem = enumerator.Current;
                    var type = firstItem.GetType();

                    if (typeof(TList) == type)
                        throw new NestedNonGenericCollectionException($"Type {type} is a non-generic collection. It contain instances of itself.");

                    // Now we can create special state for the clone generator
                    var state = new State(
                        (currentType, currentMember) =>
                        {
                            if (currentMember == _key)
                                return type;

                            if (currentMember != null)
                                return null;

                            if (currentType == _key)
                                return type;

                            return null;
                        }
                    );

                    _copier = _clonerInstance.CreateNonGenericListFillerDelegate(state, typeof(TList), type, _key as MemberInfo);
                    _copier(input, output);
                }
            }

            private static PropertyInfo DiscoverPropertyFromGetMethod(MethodInfo minfo)
            {
                return minfo
                    .DeclaringType
                    .GetProperties()
                    .Single(p => p.GetGetMethod() == minfo);
            }

            public bool Matches(object key) =>
                _key == key;

            public RuntimeIListCloner(object key, IlCloner cloner)
            {
                _clonerInstance = cloner;
                _key = key;

                if (key is MethodInfo)
                    _key = DiscoverPropertyFromGetMethod(key as MethodInfo);
            }
        }

        private class State
        {
            private readonly AssemblyBuilder _asmBuilder;
            private readonly ConstructorBuilder _staticConstructor;
            private Dictionary<Type, FieldInfo> _customCtorFields =
                new Dictionary<Type, FieldInfo>();

            /// <summary>
            /// If the <see cref="CloneDeep{T}(State, MemberInfo)"/> method is being called from a runtime
            /// cloning generator.
            /// </summary>
            public bool NeedReconstruction { get; }
            public ILGenerator StaticConstructor { get; private set; }
            public TypeBuilder TypeBuilder { get; private set; }
            public FieldInfo ClonerInstanceField { get; private set; }
            public ILGenerator Generator { get; set; }
            public int Depth { get; private set; }
            public GetElementTypeForNonGenericListDelegate ElementTypeGetter { get; private set; } =
                (a, b) => null;
            public Type RootType { get; private set; }

            public State Descend() =>
                new State()
                {
                    Depth = Depth + 1,
                    TypeBuilder = TypeBuilder,
                    StaticConstructor = StaticConstructor,
                    ElementTypeGetter = ElementTypeGetter,
                    ClonerInstanceField = ClonerInstanceField,
                    _customCtorFields = _customCtorFields,
                    RootType = RootType
                };

            public Type FinalizeType(IlCloner instance)
            {
                StaticConstructor.Emit(OpCodes.Ret);

                var rval = TypeBuilder.CreateType();

                ClonerInstanceField.SetValue(null, instance);

                foreach (var ctor in _customCtorFields)
                    rval.GetField(_customCtorFields[ctor.Key].Name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, instance._customConstructors[ctor.Key]);

                if (instance.SaveAssemblies)
                    _asmBuilder.Save("MainModule.dll");

                return rval;
            }

            public FieldInfo GetFieldForCustomConstructor(Type t)
            {
                if (!_customCtorFields.ContainsKey(t))
                {
                    var field = TypeBuilder.DefineField(
                        $"_customCtor_{t.Name}",
                        typeof(Func<,>).MakeGenericType(t, t),
                        FieldAttributes.Private | FieldAttributes.Static
                    );

                    _customCtorFields[t] = field;
                }

                return _customCtorFields[t];
            }

            private State() { }

            public delegate Type GetElementTypeForNonGenericListDelegate(Type currentType, MemberInfo currentMember);

            public State(GetElementTypeForNonGenericListDelegate test)
            {
                ElementTypeGetter = test;
                NeedReconstruction = true;
            }

            public State(string name, Type t, State withTest)
                : this(name, t)
            {
                if (!withTest.NeedReconstruction)
                    throw new ArgumentException();

                ElementTypeGetter = withTest.ElementTypeGetter;
            }

            public State(string name, Type t)
            {
                FieldInfo fb;

                RootType = t;
                TypeBuilder = CreateTypeBuilder(name, out _asmBuilder, out fb);
                ClonerInstanceField = fb;
                _staticConstructor = TypeBuilder.DefineTypeInitializer();
                StaticConstructor = _staticConstructor.GetILGenerator();
            }
        }

        #endregion // Nested classes
    }
}
