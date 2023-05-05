using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

namespace Mapper
{
    public interface IMapping<T1, T2> where T2 : new()
    {
        public T2 Map(T1 t1);
    }

    public class Member
    {
        private MemberInfo _memberInfo = null;
        private Type _rootType = null;
        private string _memberPath = null;
        public Member(MemberInfo memberInfo, Type rootType, string path)
        {
            _memberInfo = memberInfo;
            _rootType = rootType;
            _memberPath = path;
        }

        public MemberInfo MemberInfo
        {
            get
            {
                return _memberInfo;
            }
        }

        public string Path
        {
            get
            {
                return _memberPath;
            }
        }

        public Type RootType
        {
            get
            {
                return _rootType;
            }
        }
    }

    public class Mapper<T1, T2> where T2 : new()
    {
        private Dictionary<Member, Member> _mappings = new Dictionary<Member, Member>();
        private static Member FromLambdaExpression(LambdaExpression expression)
        {
            Expression expressionToCheck = expression;

            bool done = false;
            string path = string.Empty;
            MemberInfo memberInfo = null;
            Type rootType = null;
            while (!done)
            {
                switch (expressionToCheck.NodeType)
                {
                    case ExpressionType.Convert:
                        expressionToCheck = ((UnaryExpression)expressionToCheck).Operand;
                        break;
                    case ExpressionType.Lambda:
                        expressionToCheck = ((LambdaExpression)expressionToCheck).Body;
                        break;
                    case ExpressionType.MemberAccess:
                        var memberExpression = ((MemberExpression)expressionToCheck);
                        if (memberInfo == null)
                        {
                            memberInfo = memberExpression.Member;
                        }
                        else
                        {
                            path = memberExpression.Member.Name + (string.IsNullOrEmpty(path) ? "" : ".") + path;
                        }
                        rootType = memberExpression.Member.ReflectedType;
                        expressionToCheck = memberExpression.Expression;
                        break;
                    case ExpressionType.Call:
                        var methodCallExpression = (MethodCallExpression)expressionToCheck;
                        if (memberInfo == null)
                        {
                            memberInfo = methodCallExpression.Method;
                        }
                        else
                        {
                            path = methodCallExpression.Method.Name + (string.IsNullOrEmpty(path) ? "" : ".") + path;
                        }
                        rootType = methodCallExpression.Method.ReflectedType;
                        expressionToCheck = methodCallExpression.Object;
                        break;
                    case ExpressionType.Parameter:
                        //try to get the real ReflectedType since the ReflectedType could be not correct if the member is delcared in a base class
                        //http://stackoverflow.com/questions/23105567/reflectedtype-from-memberexpression
                        var parameterExpression = ((ParameterExpression)expressionToCheck);
                        rootType = parameterExpression.Type;
                        done = true;
                        break;
                    default:
                        done = true;
                        break;
                }
            }

            if ((memberInfo != null) && (rootType != null))
            {
                return new Member(memberInfo, rootType, path);
            }
            return null;
        }

        private string GenerateSourceCode()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append($@"
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mapper;
namespace Mapper
{{
    public class Mapping: IMapping<{typeof(T1).FullName},{typeof(T2).FullName}>
    {{
        public {typeof(T2).FullName} Map({typeof(T1).FullName} t1)
        {{
            {typeof(T2).FullName} t2 = new {typeof(T2).FullName}();
");

            foreach (var mapping in _mappings)
            {
                sb.Append($"t2.{mapping.Value.MemberInfo.Name} = t1.{mapping.Key.MemberInfo.Name};\r\n");
            }
            sb.Append(@"
            return t2;
        }
    }
};");
            return sb.ToString();
        }

        private IEnumerable<Type> GetParentTypes(Type type)
        {
            // is there any base type?
            if ((type == null) || (type.BaseType == null))
            {
                yield break;
            }

            // return all implemented or inherited interfaces
            foreach (var i in type.GetInterfaces())
            {
                yield return i;
            }

            // return all inherited types
            var currentBaseType = type.BaseType;
            while (currentBaseType != null)
            {
                yield return currentBaseType;
                currentBaseType = currentBaseType.BaseType;
            }
        }        
        private IEnumerable<MetadataReference> GetMetadataFileReferences()
        {
            var references = new List<MetadataReference>();
            Dictionary<string, string> paths = new Dictionary<string, string>();

            foreach (var mapping in _mappings)
            {
                if (!paths.ContainsKey(mapping.Key.RootType.Assembly.Location))
                {
                    paths.Add(mapping.Key.RootType.Assembly.Location, mapping.Key.RootType.Assembly.Location);
                }

                foreach (var baseType in GetParentTypes(mapping.Key.RootType))
                {
                    if (!paths.ContainsKey(baseType.Assembly.Location))
                    {
                        paths.Add(baseType.Assembly.Location, baseType.Assembly.Location);
                    }
                }

                if (!paths.ContainsKey(mapping.Value.RootType.Assembly.Location))
                {
                    paths.Add(mapping.Value.RootType.Assembly.Location, mapping.Value.RootType.Assembly.Location);
                }

                foreach (var baseType in GetParentTypes(mapping.Value.RootType))
                {
                    if (!paths.ContainsKey(baseType.Assembly.Location))
                    {
                        paths.Add(baseType.Assembly.Location, baseType.Assembly.Location);
                    }
                }
            }

            
            foreach (var path in paths)
            {
                references.Add(MetadataReference.CreateFromFile(path.Value));
            }

            Assembly.GetEntryAssembly().GetReferencedAssemblies()
            .ToList()
            .ForEach(a => references.Add(MetadataReference.CreateFromFile(Assembly.Load(a).Location)));

            Assembly.GetExecutingAssembly().GetReferencedAssemblies()
            .ToList()
            .ForEach(a => references.Add(MetadataReference.CreateFromFile(Assembly.Load(a).Location)));

            return references;
        }
        protected Assembly BuildAssembly(string code)
        {
            var tree = SyntaxFactory.ParseSyntaxTree(code);            

            var compilation = CSharpCompilation.Create(
                null,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                syntaxTrees: new[] { tree },
                references: GetMetadataFileReferences());

            Assembly compiledAssembly = null;
            using (var stream = new MemoryStream())
            {
                var compileResult = compilation.Emit(stream);
                if (compileResult.Success)
                {
                    compiledAssembly = Assembly.Load(stream.GetBuffer());
                }
            }
            return compiledAssembly;
        }
        public Mapper<T1, T2> Bind(Expression<Func<T1, object>> t1, Expression<Func<T2, object>> t2)
        {
            var member1 = FromLambdaExpression(t1);
            var member2 = FromLambdaExpression(t2);
            _mappings.Add(member1, member2);
            return this;
        }       

        public IMapping<T1, T2> Build()
        {
            var code = GenerateSourceCode();
            var assembly = BuildAssembly(code);
            if (assembly != null)
            {
                Type mapType = assembly.GetType($"Mapper.Mapping");
                if (mapType != null)
                {
                    IMapping<T1,T2> mapping = (IMapping<T1, T2>)Activator.CreateInstance(mapType);
                    if (mapping != null)
                    {
                        return mapping;
                    }
                }
            }
            return null;
        }
    }
}