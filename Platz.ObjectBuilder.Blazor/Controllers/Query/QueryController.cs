﻿using Microsoft.EntityFrameworkCore;
using Platz.ObjectBuilder.Blazor.Controllers;
using Platz.ObjectBuilder.Blazor.Controllers.Logic;
using Platz.ObjectBuilder.Blazor.Validation;
using Platz.ObjectBuilder.Engine;
using Platz.ObjectBuilder.Expressions;
using Platz.ObjectBuilder.Helpers;
using Platz.ObjectBuilder.Schema;
using Platz.SqlForms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Platz.ObjectBuilder
{
    public interface IQueryController //: IQueryModel
    {
        StoreQueryParameters StoreParameters { get; }
        StoreSchema Schema { get; }
        string Errors { get; set; }
        List<IQueryModel> SubQueryList { get; }
        int SelectedQueryIndex { get; set; }
        // used for UI
        IQueryModel SelectedQuery { get; }
        // used only for load/save/validate
        IQueryModel MainQuery { get; }

        List<TableLink> FromTableLinks { get; }
        List<TableJoinModel> FromTableJoins { get; }
        List<RuleValidationResult> ValidationResults { get; }
        List<QueryFromTable> FromTables { get; }
        List<QuerySelectProperty> SelectionProperties { get; }
        string WhereClause { get; }

        List<DesignQueryObject> GetAvailableQueryObjects();
        void CreateSubQuery(int index);
        void Configure(IQueryControllerConfiguration config);
        void LoadSchema();
        void AddFromTable(DesignQueryObject table);
        void RemoveFromTable(string tableName, string alias);
        QueryFromTable FindFromTable(string tableName, string alias);
        void AddSelectionProperty(QueryFromTable table, QueryFromProperty property);
        void RemoveSelectionProperty(QueryFromTable table, QueryFromProperty property);
        void ApplySelectPropertyFilter(QuerySelectProperty property, string filter);
        void SetGroupByFunction(QuerySelectProperty property, string filter);
        void SetWhereClause(string text);

        StoreQuery GenerateQuery();
        void SaveQuery(string path);
        void SaveSchema(string path);

        string GenerateObjectId(string sfx, int objId, int propId = 0);

        void AliasChanged(string oldAlias, string newAlias);
        void RegenerateTableLinks();
        void Validate();
        List<string> GetFileList(string path);
        void LoadFromFile(string path, string fileName);
        bool FileExists(string path);
        string GenerateFileName(string path);
        void Clear();
    }
 
    public class QueryController : IQueryController
    {
        public StoreQueryParameters StoreParameters { get; set; } = new StoreQueryParameters();
        public StoreSchema Schema { get; private set; }
        public List<QueryFromTable> FromTables { get { return SelectedQuery.FromTables; } }
        public List<QuerySelectProperty> SelectionProperties { get { return SelectedQuery.SelectionProperties; } }
        public List<TableLink> FromTableLinks { get { return SelectedQuery.FromTableLinks; } }
        public List<TableJoinModel> FromTableJoins { get { return SelectedQuery.FromTableJoins; } }
        public string WhereClause { get { return SelectedQuery.WhereClause; } }

        public List<RuleValidationResult> ValidationResults { get; private set; } = new List<RuleValidationResult>();
        public string Errors { get; set; } = "";


        public List<IQueryModel> SubQueryList { get; private set; }
        public int SelectedQueryIndex { get; set; } = 0;
        public IQueryModel SelectedQuery { get { return SubQueryList[SelectedQueryIndex]; } }
        public IQueryModel MainQuery { get; private set; }




        private IStoreSchemaReader _reader;
        private IStoreSchemaStorage _storage;
        private IStoreSchemaReaderParameters _readerParameters;
        private IObjectResolver _resolver;
        private SqlExpressionEngine _expressions;
        private readonly IQueryBuilderEngine _engine;

        public QueryController(IQueryBuilderEngine engine)
        {
            _engine = engine;
            SetNewQuery();
        }

        public void Configure(IQueryControllerConfiguration config)
        {
            _reader = config.Reader;
            _storage = config.Storage;
            _readerParameters = config.ReaderParameters;
            _resolver = config.Resolver;
            _expressions = config.ExpressionEngine;
        }

        public List<DesignQueryObject> GetAvailableQueryObjects()
        {
            var list = Schema.Definitions.Values.Select(d => new DesignQueryObject(d)).ToList();

            for (int i = 1; i < SubQueryList.Count; i++)
            {
                if (i != SelectedQueryIndex)
                {
                    var q = new DesignQueryObject(SubQueryList[i]);
                    list.Add(q);
                }
            }

            return list;
        }

        private void SetNewQuery()
        {
            SelectedQueryIndex = 0;
            SubQueryList = new List<IQueryModel>();
            MainQuery = new QueryModel { Name = "Main" };
            MainQuery.Schema = Schema;
            SubQueryList.Add(MainQuery);
        }

        public void CreateSubQuery(int index)
        {
            var q = new QueryModel { Name = $"Query{index}" };
            SubQueryList.Add(q);
        }

        public void Clear()
        {
            // clear all values in controller
            StoreParameters.DataService = "MyDataService";
            StoreParameters.QueryName = "";
            StoreParameters.Namespace = "Default";
            StoreParameters.QueryReturnType = "";

            //FromTables.Clear();
            //SelectionProperties.Clear();
            //FromTableLinks.Clear();
            //FromTableJoins.Clear();
            //WhereClause = "";

            SetNewQuery();
        }

        public void LoadFromFile(string path, string fileName)
        {
            var parameters = new StorageParameters { FileName = fileName, Path = path };
            var q = _storage.LoadQuery(parameters);
            Clear();
            var queryModel = _engine.LoadFromStoreQuery(MainQuery, q);
            StoreParameters = queryModel.StoreParameters;
            MainQuery.FromTables = queryModel.FromTables;
            RegenerateTableLinks();
            MainQuery.SelectionProperties = queryModel.SelectionProperties;

            foreach (var sp in MainQuery.SelectionProperties)
            {
                // var t = FindFromTable(sp.);
                var prop = sp.FromTable.Properties.SingleOrDefault(p => p.StoreProperty.Name == sp.StoreProperty.Name);

                if (prop != null)
                {
                    prop.Selected = true;
                }
            }

            MainQuery.WhereClause = queryModel.WhereClause;
        }

        public bool FileExists(string path)
        {
            return _storage.FileExists(new StorageParameters { Path = path, FileName = GenerateFileName(path) });
        }

        public List<string> GetFileList(string path)
        {
            var result = _storage.GetFileNames(new StorageParameters { Path = path });
            result = result.Where(f => !f.ToLower().Contains("schema.json") && !f.ToLower().Contains("schema.migrations.json")).ToList();
            return result;
        }

        public void AliasChanged(string oldAlias, string newAlias)
        {
            var table = SelectedQuery.FromTables.Single(t => t.Alias == oldAlias);
            table.Alias = newAlias;

            foreach (var j in SelectedQuery.FromTableJoins)
            {
                if (j.Source.LeftObjectAlias == oldAlias)
                {
                    j.Source.LeftObjectAlias = newAlias;
                }

                if (j.Source.RightObjectAlias == oldAlias)
                {
                    j.Source.RightObjectAlias = newAlias;
                }
            }

        }

        public string GenerateObjectId(string prefix, int objId, int propId = 0)
        {
            var id = $"{prefix}_{objId}_{propId}";
            return id;
        }

        public void SaveQuery(string path)
        {
            var fileName = GenerateFileName(path);
            var parameters = new StorageParameters { FileName = fileName };
            var query = GenerateQuery();
            _storage.SaveQuery(query, parameters);
        }

        public string GenerateFileName(string path)
        {
            return Path.Combine(path, $"{StoreParameters.QueryName}.json");
        }

        public void SaveSchema(string path)
        {
            var fileName = Path.Combine(path, $"Schema.json");
            var parameters = new StorageParameters { FileName = fileName };
            _storage.SaveSchema(Schema, parameters);
        }

        public void LoadSchema()
        {
            Schema = _reader.ReadSchema(_readerParameters);
        }

        public StoreQuery GenerateQuery()
        {
            Validate();

            if (ValidationResults.Any())
            {
                return null;
            }

            return _engine.GenerateQuery(MainQuery);
        }

        public void Validate()
        {
            ValidationResults = _engine.Validate(MainQuery);
        }

        private List<StoreObjectJoin> GenerateJoins()
        {
            var joins = new List<StoreObjectJoin>();
            var tables = MainQuery.FromTables.ToList();

            var foreignKeys = tables.SelectMany(t => t.Properties, (t, p) => new { Tbl = t, Prop = p }).Where(p =>
                p.Prop.StoreProperty.Fk && tables.Any(d => d.StoreDefinition.Name == p.Prop.StoreProperty.ForeignKeys.First().DefinitionName)).ToList();

            foreach (var reference in foreignKeys)
            {
                var leftTable = tables.First(t => t.StoreDefinition.Name == reference.Prop.StoreProperty.ForeignKeys.First().DefinitionName);
                var rightTable = tables.First(t => t.StoreDefinition.Name == reference.Tbl.StoreDefinition.Name);

                var join = new StoreObjectJoin
                {
                    JoinType = "inner",
                    LeftObjectAlias = leftTable.Alias,
                    LeftField = reference.Prop.StoreProperty.ForeignKeys.First().PropertyName,
                    RightObjectAlias = rightTable.Alias,
                    RightField = reference.Prop.StoreProperty.Name
                };

                joins.Add(join);
            }

            return joins;
        }

        public void AddFromTable(DesignQueryObject qo)
        {
            if (qo.IsSubQuery)
            {

            }
            else
            {
                var ft = new QueryFromTable(qo.Table);
                ft.Alias = GetDefaultAlias(ft);
                SelectedQuery.FromTables.Add(ft);
                RegenerateTableLinks();
                _engine.SelectPropertiesFromNewTable(this, ft);
            }
        }

        public void RemoveFromTable(string tableName, string alias)
        {
            var table = SelectedQuery.FromTables.Single(t => t.Alias == alias && t.StoreDefinition.Name == tableName);
            SelectedQuery.FromTables.Remove(table);
            RegenerateTableLinks();
        }

        public QueryFromTable FindFromTable(string tableName, string alias)
        {
            var table = SelectedQuery.FromTables.SingleOrDefault(t => t.Alias == alias && t.StoreDefinition.Name == tableName);
            return table;
        }

        public void RegenerateTableLinks()
        {
            SelectedQuery.FromTableLinks = new List<TableLink>();
            var joins = GenerateJoins();

            var newJoins = joins.Where(j => !SelectedQuery.FromTableJoins.Any(f => f.Source.GetJoinString() == j.GetJoinString()));
            SelectedQuery.FromTableJoins.AddRange(newJoins.Select(j => new TableJoinModel { Source = j, JoinType = "Inner" }));
            var lostJoins = SelectedQuery.FromTableJoins.Where(j => !SelectedQuery.FromTables.Any(t => t.Alias == j.Source.LeftObjectAlias) || !SelectedQuery.FromTables.Any(t => t.Alias == j.Source.RightObjectAlias));
            SelectedQuery.FromTableJoins = SelectedQuery.FromTableJoins.Except(lostJoins).ToList();

            foreach (var fj in SelectedQuery.FromTableJoins.Where(f => !f.IsDeleted))
            {
                var join = fj.Source;
                var pt = SelectedQuery.FromTables.First(t => t.Alias == join.LeftObjectAlias);
                var pk = pt.Properties.First(p => p.StoreProperty.Name == join.LeftField);
                var pkIndex = pt.Properties.IndexOf(pk);

                var ft = SelectedQuery.FromTables.First(t => t.Alias == join.RightObjectAlias);
                var fk = ft.Properties.First(p => p.StoreProperty.Name == join.RightField);
                var fkIndex = ft.Properties.IndexOf(fk);

                var link = new TableLink 
                { 
                    PrimaryRefId = GenerateObjectId("table_primary", pt.Id, pkIndex),
                    ForeignRefId = GenerateObjectId("table_foreign", ft.Id, fkIndex),
                    Source = join
                };

                SelectedQuery.FromTableLinks.Add(link);
            }
        }

        private string GetDefaultAlias(QueryFromTable ft)
        {
            var count = SelectedQuery.FromTables.Where(t => t.StoreDefinition.Name == ft.StoreDefinition.Name).Count();
            var sfx = count > 0 ? (count + 1).ToString() : "";
            var used = SelectedQuery.FromTables.Select(t => t.Alias).ToList();

            for (int i = 1; i <= 5; i++)
            {
                var alias = ft.StoreDefinition.Name.Substring(0, i).ToLower() + sfx;

                if (!used.Contains(alias))
                {
                    return alias;
                }
            }

            // alias not found
            return "";
        }

        public void AddSelectionProperty(QueryFromTable table, QueryFromProperty property)
        {
            if (!SelectedQuery.SelectionProperties.Any(s => s.FromTable.Alias == table.Alias && s.StoreProperty.Name == property.StoreProperty.Name))
            {
                var newSelectProperty = new QuerySelectProperty(table, property.StoreProperty) { IsOutput = true, Alias = property.Alias };
                SelectedQuery.SelectionProperties.Add(newSelectProperty);
            }
        }

        public void RemoveSelectionProperty(QueryFromTable table, QueryFromProperty property)
        {
            var item = SelectedQuery.SelectionProperties.FirstOrDefault(s => s.StoreProperty.Name == property.StoreProperty.Name && s.FromTable.Alias == table.Alias);

            if (item != null)
            {
                SelectedQuery.SelectionProperties.Remove(item);
            }
        }

        public void ApplySelectPropertyFilter(QuerySelectProperty property, string filterText)
        {
            var filter = filterText?.Trim();

            if (string.IsNullOrWhiteSpace(filter))
            {
                // find and remove previous condition
            }
            else
            {
                // add new condition
                var filterOperator = "=";

                var operators = _resolver.GetCompareOperators();
                
                foreach (var o in operators)
                {
                    if (filter.StartsWith(o))
                    {
                        filterOperator = o;
                        filter = filter.Substring(o.Count()).Trim();
                        break;
                    }
                };

                var filterValue = filter;
                char quote = _resolver.GetStringLiteralSymbol();

                if (filterValue.First() != '@' && (property.StoreProperty.Type == "String" || property.StoreProperty.Type == "DateTime"))
                {
                    if (filterValue.First() != quote)
                    {
                        filterValue = quote + filterValue;
                    }

                    if (filterValue.Last() != quote)
                    {
                        filterValue = filterValue + quote;
                    }
                }

                if ( !string.IsNullOrWhiteSpace(SelectedQuery.WhereClause))
                {
                    SelectedQuery.WhereClause += " AND ";
                }

                SelectedQuery.WhereClause += $"{property.FromTable.Alias}.{property.StoreProperty.Name} {filterOperator} {filterValue}";
            }
        }

        public void SetWhereClause(string text)
        {
            SelectedQuery.WhereClause = text;
            CheckWhereClause();
        }

        private void CheckWhereClause()
        {
            try
            {
                var result = _expressions.BuildExpressionTree(SelectedQuery.WhereClause);
            }
            catch(Exception exc)
            {
                Errors += "\r\n" + exc.Message;
            }
        }

        public void SetGroupByFunction(QuerySelectProperty property, string func)
        {
            property.GroupByFunction = func;

            if (func == "Group By All")
            {
                SelectedQuery.SelectionProperties.Where(p => p.IsOutput).ToList().ForEach(p => p.GroupByFunction = "Group By");
                SelectedQuery.SelectionProperties.Where(p => !String.IsNullOrWhiteSpace(p.Filter)).ToList().ForEach(p => p.GroupByFunction = "Where");
            }

            if (func == "Group By None")
            {
                SelectedQuery.SelectionProperties.ToList().ForEach(p => p.GroupByFunction = "");
            }
        }
    }

    
}
