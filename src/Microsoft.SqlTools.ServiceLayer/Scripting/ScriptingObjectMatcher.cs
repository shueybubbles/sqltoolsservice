﻿//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlTools.ServiceLayer.Scripting.Contracts;
using Microsoft.SqlTools.Utility;

namespace Microsoft.SqlTools.ServiceLayer.Scripting
{
    /// <summary>
    /// Implements matching logic to filter scripting objects based on an 
    /// include/exclude criteria.
    /// </summary>
    /// <remarks>
    /// First, objects are included by the include filter.  Then, objects are removed by
    /// the exclude filter.  Matches are made by comparing case insensitive strings for the 
    /// ScriptingObject Type, Schema, and Name properties.  Wildcards '*' are supported for 
    /// the ScriptingObject Schema and Name properties.  Matching on ScriptingObject Type 
    /// property must be an exact match.
    /// 
    /// Examples:
    ///     
    /// Include ScriptingObject { Type = null, Schema = "dbo", Name = null } 
    /// -> matches all objects in the dbo schema.
    ///     
    /// Include ScriptingObject { Type = "Table", Schema = "dbo", Name = null } 
    /// -> matches all tables in the dbo schema.
    /// 
    /// Include ScriptingObject { Type = "Table", Schema = null, Name = "Emp*" } 
    /// -> matches all table names that start with "Emp"
    ///
    /// Include ScriptingObject { Type = "View", Schema = null, Name = "Emp*" } 
    /// Include ScriptingObject { Type = "Table", Schema = null, Name = "Emp*" } 
    /// -> matches all table and views with names that start with "Emp"
    ///
    /// Include ScriptingObject { Type = "Table", Schema = null, Name = null }
    /// Exclude ScriptingObject { Type = null, Schema = "HumanResources", Name = null } 
    /// -> matches all tables except tables in the "HumanResources" schema
    ///
    /// </remarks>
    public static class ScriptingObjectMatcher
    {
        private const string Wildcard = "*";

        /// <summary>
        /// Given a collection of candidate scripting objects, filters the items that match 
        /// based on the passed include and exclude criteria.
        /// </summary>
        /// <param name="includeCriteria">The include object criteria.</param>
        /// <param name="excludeCriteria">The exclude object criteria.</param>
        /// <param name="includeSchema">The include schema filter.</param>
        /// <param name="excludeSchema">The exclude schema filter.</param>
        /// <param name="includeType">The include type filter.</param>
        /// <param name="excludeType">The exclude type filter.</param>
        /// <param name="candidates">The candidate object to filter.</param>
        /// <returns>The matching scripting objects.</returns>
        public static IEnumerable<ScriptingObject> Match(
            ScriptingObject includeCriteria,
            ScriptingObject excludeCriteria,
            string includeSchema,
            string excludeSchema,
            string includeType,
            string excludeType,
            IEnumerable<ScriptingObject> candidates)
        {
            return Match(
                includeCriteria == null ? new ScriptingObject[0] : new[] { includeCriteria },
                excludeCriteria == null ? new ScriptingObject[0] : new[] { excludeCriteria },
                includeSchema == null ? new List<string>(): new List<string> { includeSchema },
                excludeSchema == null ? new List<string>(): new List<string> { excludeSchema },
                includeType == null ? new List<string>(): new List<string> { includeType },
                excludeType == null ? new List<string>(): new List<string> { excludeType },
                candidates);
        }

        /// <summary>
        /// Given a collection of candidate scripting objects, filters the items that match 
        /// based on the passed include and exclude criteria.
        /// </summary>
        /// <param name="includeCriteria">The collection of include object criteria items.</param>
        /// <param name="excludeCriteria">The collection of exclude object criteria items.</param>
        /// <param name="includeSchema">The collection of include schema items.</param>
        /// <param name="excludeSchema">The collection of exclude schema items.</param>
        /// <param name="includeType">The collection of include type items.</param>
        /// <param name="excludeType">The collection of exclude type items.</param>
        /// <param name="candidates">The candidate object to filter.</param>
        /// <returns>The matching scripting objects.</returns>
        public static IEnumerable<ScriptingObject> Match(
            IEnumerable<ScriptingObject> includeCriteria,
            IEnumerable<ScriptingObject> excludeCriteria,
            IEnumerable<string> includeSchema,
            IEnumerable<string> excludeSchema,
            IEnumerable<string> includeType,
            IEnumerable<string> excludeType,
            IEnumerable<ScriptingObject> candidates)
        {
            Validate.IsNotNull("candidates", candidates);
            
            IEnumerable<ScriptingObject> matchedObjects = new List<ScriptingObject>();

            if (includeCriteria != null && includeCriteria.Any())
            {
                foreach (ScriptingObject scriptingObjectCriteria in includeCriteria)
                {
                    IEnumerable<ScriptingObject> matches = MatchCriteria(scriptingObjectCriteria, candidates);
                    matchedObjects = matchedObjects.Union(matches);
                }
            }
            else
            {
                matchedObjects = candidates;
            }

            if (excludeCriteria != null && excludeCriteria.Any())
            {
                foreach (ScriptingObject scriptingObjectCriteria in excludeCriteria)
                {
                    IEnumerable<ScriptingObject> matches = MatchCriteria(scriptingObjectCriteria, matchedObjects);
                    matchedObjects = matchedObjects.Except(matches);
                }
            }

            // Apply additional filters if included.
            matchedObjects = ExcludeSchemaAndOrType(excludeSchema, excludeType, matchedObjects);
            matchedObjects = IncludeSchemaAndOrType(includeSchema, includeType, matchedObjects);

            return matchedObjects;
        }

        private static IEnumerable<ScriptingObject> ExcludeSchemaAndOrType(IEnumerable<string> excludeSchema, IEnumerable<string> excludeType, 
            IEnumerable<ScriptingObject> candidates)
        {
            // Given a list of candidates, we remove any objects that match the excluded schema and/or type.
            IEnumerable<ScriptingObject> remainingObjects = candidates;
            IEnumerable<ScriptingObject> matches = null;

            if (excludeSchema != null && excludeSchema.Any())
            {            
                foreach (string exclude_schema in excludeSchema)
                {
                    matches = MatchCriteria(exclude_schema, (candidate) => { return candidate.Schema; }, candidates);
                    remainingObjects = remainingObjects.Except(matches);
                }
            }

            if (excludeType != null && excludeType.Any())
            {
                foreach (string exclude_type in excludeType)
                {
                    matches = remainingObjects.Where(o => string.Equals(exclude_type, o.Type, StringComparison.OrdinalIgnoreCase));
                    remainingObjects = remainingObjects.Except(matches);           
                }
            }

            return remainingObjects;
        }

        private static IEnumerable<ScriptingObject> IncludeSchemaAndOrType(IEnumerable<string> includeSchema, IEnumerable<string> includeType, 
            IEnumerable<ScriptingObject> candidates)
        {
            // Given a list of candidates, we return a new list of scripting objects that match
            // the schema and/or type filter.
            IEnumerable<ScriptingObject> matchedSchema = new List<ScriptingObject>();
            IEnumerable<ScriptingObject> matchedType = new List<ScriptingObject>();
            IEnumerable<ScriptingObject> matchedObjects = new List<ScriptingObject>();
            IEnumerable<ScriptingObject> matches = null;

            if (includeSchema != null && includeSchema.Any())
            {            
                foreach (string include_schema in includeSchema)
                {
                    matches = MatchCriteria(include_schema, (candidate) => { return candidate.Schema; }, candidates);
                    matchedSchema = matchedSchema.Union(matches);
                }
                matchedObjects = matchedSchema;
            }
            else
            {
                matchedObjects = candidates;
            }

            if (includeType != null && includeType.Any())
            {
                foreach (string include_type in includeType)
                {
                    matches = matchedObjects.Where(o => string.Equals(include_type, o.Type, StringComparison.OrdinalIgnoreCase));
                    matchedType = matchedType.Union(matches);
                }
                matchedObjects = matchedType;
            }

            return matchedObjects;
        }

        private static IEnumerable<ScriptingObject> MatchCriteria(ScriptingObject criteria, IEnumerable<ScriptingObject> candidates)
        {
            Validate.IsNotNull("criteria", criteria);
            Validate.IsNotNull("candidates", candidates);

            IEnumerable<ScriptingObject> matchedObjects = candidates;

            if (!string.IsNullOrWhiteSpace(criteria.Type))
            {
                matchedObjects = matchedObjects.Where(o => string.Equals(criteria.Type, o.Type, StringComparison.OrdinalIgnoreCase));
            }

            matchedObjects = MatchCriteria(criteria.Schema, (candidate) => { return candidate.Schema; }, matchedObjects);
            matchedObjects = MatchCriteria(criteria.Name, (candidate) => { return candidate.Name; }, matchedObjects);

            return matchedObjects;
        }

        private static IEnumerable<ScriptingObject> MatchCriteria(string property, Func<ScriptingObject, string> propertySelector, IEnumerable<ScriptingObject> candidates)
        {
            IEnumerable<ScriptingObject> matchedObjects = candidates;

            if (!string.IsNullOrWhiteSpace(property))
            {
                if (property.Equals(Wildcard, StringComparison.OrdinalIgnoreCase))
                {
                    // Don't filter any objects
                }
                if (property.EndsWith(Wildcard, StringComparison.OrdinalIgnoreCase))
                {
                    matchedObjects = candidates.Where(o => propertySelector(o).StartsWith(
                        propertySelector(o).Substring(0, propertySelector(o).Length - 1),
                        StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    matchedObjects = matchedObjects.Where(o => string.Equals(property, propertySelector(o), StringComparison.OrdinalIgnoreCase));
                }
            }

            return matchedObjects;
        }
    }
}
