// 
// MemberCollector.cs
//  
// Author:
//       Mansheng Yang <lightyang0@gmail.com>
// 
// Copyright (c) 2012 Mansheng Yang
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Collections.Generic;
using System.Linq;
using MonoDevelop.Projects;
using MonoDevelop.TypeSystem;
using ICSharpCode.NRefactory.TypeSystem;

namespace MonoDevelop.Ide.FindInFiles
{
	public static class MemberCollector
	{

		static bool MatchParameters (IMember a, IMember b)
		{
			return MatchParameters (a as IParameterizedMember, b as IParameterizedMember);
		}

		static bool MatchParameters (IParameterizedMember a, IParameterizedMember b)
		{
			if (a == null && b == null) return true;
			if (a == null || b == null) return false;
			return ParameterListComparer.Instance.Equals (a.Parameters, b.Parameters);
		}

		/// <summary>
		/// find all base types(types that are not derived from other types) in the specified types
		/// </summary>
		/// <param name="types"></param>
		/// <returns></returns>
		public static IEnumerable<ITypeDefinition> GetBaseTypes (IEnumerable<ITypeDefinition> types)
		{
			if (!types.Any())
				yield break;
			
			var baseType = types.FirstOrDefault ();
			var otherTypes = new List<ITypeDefinition> ();
			
			foreach (var type in types.Skip (1)) {
				if (baseType.IsDerivedFrom (type)) {
					baseType = type;
				} else if (!type.IsDerivedFrom (baseType)) {
					// this type is not directly related to baseType
					otherTypes.Add (type);
				}
			}
			yield return baseType;
			foreach (var type in GetBaseTypes (otherTypes)) 
				yield return type;
		}

		static IEnumerable<IMember> GetMembers (ITypeDefinition type, string name, bool ignoreInherited, 
												Func<IMember, bool> filter)
		{
			GetMemberOptions options = ignoreInherited ? GetMemberOptions.IgnoreInheritedMembers : GetMemberOptions.None;
			var members = type.GetMembers (m => m.Name == name, options);
			if (filter != null)
				members = members.Where (filter);
			return members;
		}

		/// <summary>
		/// collect members with the same signature/name(if overloads are included) as the specified member
		/// in the inheritance tree
		/// </summary>
		public static IEnumerable<IMember> CollectMembers (Solution solution, IMember member, bool includeOverloads = true)
		{
			Func<IMember, bool> memberFilter = null;
			if (member is IParameterizedMember && !includeOverloads)
				memberFilter = m => MatchParameters (m, member);
			
			var declaringType = member.DeclaringTypeDefinition;
			if (declaringType.Kind != TypeKind.Class && declaringType.Kind != TypeKind.Interface)
				return GetMembers (declaringType, member.Name, false, memberFilter);

			var searchTypes = new List<ITypeDefinition> ();
			var interfaces = from t in declaringType.GetAllBaseTypeDefinitions () 
							 where t.Kind == TypeKind.Interface && GetMembers (t, member.Name, true, memberFilter).Any()
						     select t;
			searchTypes.AddRange (GetBaseTypes (interfaces));

			if (member.DeclaringType.Kind == TypeKind.Class) {
				var members = GetMembers (declaringType, member.Name, false, memberFilter);
				if (members.Any (m => m.IsOverridable))
					searchTypes.AddRange (GetBaseTypes (members.Select (m => m.DeclaringTypeDefinition)));
				else if (searchTypes.Count == 0)
					return members;
			}
			
			var result = new List<IMember> ();
			var searchedTypes = new HashSet<string> ();
			foreach (var p in solution.GetAllSolutionItems<Project> ()) {
				var compilation = TypeSystemService.GetCompilation (p);

				var baseTypeImports = from t in searchTypes
									  let import = compilation.Import (t)
									  where import != null
									  select import;
				if (!baseTypeImports.Any ()) continue;

				foreach (var type in compilation.GetAllTypeDefinitions ()) {
					if (searchedTypes.Contains (type.ReflectionName)) 
						continue;
					// members in base types will also be added
					// because IsDerivedFrom return true for a type itself
					if (baseTypeImports.Any (baseType => type.IsDerivedFrom (baseType))) {
						searchedTypes.Add (type.ReflectionName);
						result.AddRange (GetMembers (type, member.Name, true, memberFilter));
					}
				}
			}
			return result;
		}
	}
}
