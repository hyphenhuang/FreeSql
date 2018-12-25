﻿using FreeSql.DatabaseModel;
using FreeSql.Internal;
using FreeSql.Internal.Model;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FreeSql.MySql {

	class MySqlCodeFirst : ICodeFirst {
		IFreeSql _orm;
		protected CommonUtils _commonUtils;
		protected CommonExpression _commonExpression;
		public MySqlCodeFirst(IFreeSql orm, CommonUtils commonUtils, CommonExpression commonExpression) {
			_orm = orm;
			_commonUtils = commonUtils;
			_commonExpression = commonExpression;
		}

		public bool IsAutoSyncStructure { get; set; } = true;

		static object _dicCsToDbLock = new object();
		static Dictionary<string, (MySqlDbType type, string dbtype, string dbtypeFull, bool? isUnsigned, bool? isnullable)> _dicCsToDb = new Dictionary<string, (MySqlDbType type, string dbtype, string dbtypeFull, bool? isUnsigned, bool? isnullable)>() {
				{ typeof(bool).FullName,  (MySqlDbType.Bit, "bit","bit(1) NOT NULL", null, false) },{ typeof(bool?).FullName,  (MySqlDbType.Bit, "bit","bit(1)", null, true) },

				{ typeof(sbyte).FullName,  (MySqlDbType.Byte, "tinyint", "tinyint(3) NOT NULL", false, false) },{ typeof(sbyte?).FullName,  (MySqlDbType.Byte, "tinyint", "tinyint(3)", false, true) },
				{ typeof(short).FullName,  (MySqlDbType.Int16, "smallint","smallint(6) NOT NULL", false, false) },{ typeof(short?).FullName,  (MySqlDbType.Int16, "smallint", "smallint(6)", false, true) },
				{ typeof(int).FullName,  (MySqlDbType.Int32, "int", "int(11) NOT NULL", false, false) },{ typeof(int?).FullName,  (MySqlDbType.Int32, "int", "int(11)", false, true) },
				{ typeof(long).FullName,  (MySqlDbType.Int64, "bigint","bigint(20) NOT NULL", false, false) },{ typeof(long?).FullName,  (MySqlDbType.Int64, "bigint","bigint(20)", false, true) },

				{ typeof(byte).FullName,  (MySqlDbType.UByte, "tinyint","tinyint(3) unsigned NOT NULL", true, false) },{ typeof(byte?).FullName,  (MySqlDbType.UByte, "tinyint","tinyint(3) unsigned", true, true) },
				{ typeof(ushort).FullName,  (MySqlDbType.UInt16, "smallint","smallint(5) unsigned NOT NULL", true, false) },{ typeof(ushort?).FullName,  (MySqlDbType.UInt16, "smallint", "smallint(5) unsigned", true, true) },
				{ typeof(uint).FullName,  (MySqlDbType.UInt32, "int", "int(10) unsigned NOT NULL", true, false) },{ typeof(uint?).FullName,  (MySqlDbType.UInt32, "int", "int(10) unsigned", true, true) },
				{ typeof(ulong).FullName,  (MySqlDbType.UInt64, "bigint", "bigint(20) unsigned NOT NULL", true, false) },{ typeof(ulong?).FullName,  (MySqlDbType.UInt64, "bigint", "bigint(20) unsigned", true, true) },

				{ typeof(double).FullName,  (MySqlDbType.Double, "double", "double NOT NULL", false, false) },{ typeof(double?).FullName,  (MySqlDbType.Double, "double", "double", false, true) },
				{ typeof(float).FullName,  (MySqlDbType.Float, "float","float NOT NULL", false, false) },{ typeof(float?).FullName,  (MySqlDbType.Float, "float","float", false, true) },
				{ typeof(decimal).FullName,  (MySqlDbType.Decimal, "decimal", "decimal(10,2) NOT NULL", false, false) },{ typeof(decimal?).FullName,  (MySqlDbType.Decimal, "decimal", "decimal(10,2)", false, true) },

				{ typeof(TimeSpan).FullName,  (MySqlDbType.Time, "time","time NOT NULL", false, false) },{ typeof(TimeSpan?).FullName,  (MySqlDbType.Time, "time", "time",false, true) },
				{ typeof(DateTime).FullName,  (MySqlDbType.DateTime, "datetime", "datetime NOT NULL", false, false) },{ typeof(DateTime?).FullName,  (MySqlDbType.DateTime, "datetime", "datetime", false, true) },

				{ typeof(byte[]).FullName,  (MySqlDbType.VarBinary, "varbinary", "varbinary(255)", false, null) },
				{ typeof(string).FullName,  (MySqlDbType.VarChar, "varchar", "varchar(255)", false, null) },

				{ typeof(Guid).FullName,  (MySqlDbType.VarChar, "char", "char(36)", false, false) },{ typeof(Guid?).FullName,  (MySqlDbType.VarChar, "char", "char(36)", false, true) },

				{ typeof(MygisPoint).FullName,  (MySqlDbType.Geometry, "point", "point", false, null) },
				{ typeof(MygisLineString).FullName,  (MySqlDbType.Geometry, "linestring", "linestring", false, null) },
				{ typeof(MygisPolygon).FullName,  (MySqlDbType.Geometry, "polygon", "polygon", false, null) },
				{ typeof(MygisMultiPoint).FullName,  (MySqlDbType.Geometry, "multipoint","multipoint", false, null) },
				{ typeof(MygisMultiLineString).FullName,  (MySqlDbType.Geometry, "multilinestring","multilinestring", false, null) },
				{ typeof(MygisMultiPolygon).FullName,  (MySqlDbType.Geometry, "multipolygon", "multipolygon", false, null) },
			};

		public (int type, string dbtype, string dbtypeFull, bool? isnullable)? GetDbInfo(Type type) {
			if (_dicCsToDb.TryGetValue(type.FullName, out var trydc)) return new (int, string, string, bool?)?(((int)trydc.type, trydc.dbtype, trydc.dbtypeFull, trydc.isnullable));
			var enumType = type.IsEnum ? type : null;
			if (enumType == null && type.FullName.StartsWith("System.Nullable`1[") && type.GenericTypeArguments.Length == 1 && type.GenericTypeArguments.First().IsEnum) enumType = type.GenericTypeArguments.First();
			if (enumType != null) {
				var names = string.Join(",", Enum.GetNames(enumType).Select(a => _commonUtils.FormatSql("{0}", a)));
				var newItem = enumType.GetCustomAttributes(typeof(FlagsAttribute), false).Any() ?
					(MySqlDbType.Set, "set", $"set({names}){(type.IsEnum ? " NOT NULL" : "")}", false, type.IsEnum ? false : true) :
					(MySqlDbType.Enum, "enum", $"enum({names}){(type.IsEnum ? " NOT NULL" : "")}", false, type.IsEnum ? false : true);
				if (_dicCsToDb.ContainsKey(type.FullName) == false) {
					lock (_dicCsToDbLock) {
						if (_dicCsToDb.ContainsKey(type.FullName) == false)
							_dicCsToDb.Add(type.FullName, newItem);
					}
				}
				return ((int)newItem.Item1, newItem.Item2, newItem.Item3, newItem.Item5);
			}
			return null;
		}

		public string GetComparisonDDLStatements<TEntity>() => this.GetComparisonDDLStatements(typeof(TEntity));
		public string GetComparisonDDLStatements(params Type[] entityTypes) {
			var conn = _orm.Ado.MasterPool.Get(TimeSpan.FromSeconds(5));
			var database = conn.Value.Database;
			Func<string, string, object> ExecuteScalar = (db, sql) => {
				if (string.Compare(database, db) != 0) try { conn.Value.ChangeDatabase(db); } catch { }
				try {
					using (var cmd = conn.Value.CreateCommand()) {
						cmd.CommandText = sql;
						cmd.CommandType = CommandType.Text;
						return cmd.ExecuteScalar();
					}
				} finally {
					if (string.Compare(database, db) != 0) conn.Value.ChangeDatabase(database);
				}
			};
			var sb = new StringBuilder();
			try {
				foreach (var entityType in entityTypes) {
					if (sb.Length > 0) sb.Append("\r\n");
					var tb = _commonUtils.GetTableByEntity(entityType);
					var tbname = tb.DbName.Split(new[] { '.' }, 2);
					if (tbname?.Length == 1) tbname = new[] { database, tbname[0] };

					var tboldname = tb.DbOldName?.Split(new[] { '.' }, 2); //旧表名
					if (tboldname?.Length == 1) tboldname = new[] { database, tboldname[0] };

					if (string.Compare(tbname[0], database, true) != 0 && ExecuteScalar(database, $"select 1 from pg_database where datname='{tbname[0]}'") == null) //创建数据库
						sb.Append($"CREATE DATABASE IF NOT EXISTS ").Append(_commonUtils.QuoteSqlName(tbname[0])).Append(" default charset utf8 COLLATE utf8_general_ci;\r\n");

					var sbalter = new StringBuilder();
					var istmpatler = false; //创建临时表，导入数据，删除旧表，修改
					if (ExecuteScalar(tbname[0], "SELECT 1 FROM information_schema.TABLES WHERE table_schema={0} and table_name={1}".FormatMySql(tbname)) == null) { //表不存在
						if (tboldname != null) {
							if (string.Compare(tboldname[0], tbname[0], true) != 0 && ExecuteScalar(database, $"select 1 from information_schema.schemata where schema_name='{tboldname[0]}'") == null ||
								ExecuteScalar(tboldname[0], "SELECT 1 FROM information_schema.TABLES WHERE table_schema={0} and table_name={1}".FormatMySql(tboldname)) == null)
								//数据库或模式或表不存在
								tboldname = null;
						}
						if (tboldname == null) {
							//创建表
							sb.Append("CREATE TABLE IF NOT EXISTS ").Append(_commonUtils.QuoteSqlName($"{tbname[0]}.{tbname[1]}")).Append(" (");
							foreach (var tbcol in tb.Columns.Values) {
								sb.Append(" \r\n  ").Append(_commonUtils.QuoteSqlName(tbcol.Attribute.Name)).Append(" ");
								sb.Append(tbcol.Attribute.DbType);
								if (tbcol.Attribute.IsIdentity && tbcol.Attribute.DbType.IndexOf("AUTO_INCREMENT", StringComparison.CurrentCultureIgnoreCase) == -1) sb.Append(" AUTO_INCREMENT");
								sb.Append(",");
							}
							if (tb.Primarys.Any() == false)
								sb.Remove(sb.Length - 1, 1);
							else {
								sb.Append(" \r\n  PRIMARY KEY (");
								foreach (var tbcol in tb.Primarys) sb.Append(_commonUtils.QuoteSqlName(tbcol.Attribute.Name)).Append(", ");
								sb.Remove(sb.Length - 2, 2).Append(")");
							}
							sb.Append("\r\n) Engine=InnoDB CHARACTER SET utf8;\r\n");
							continue;
						}
						//如果新表，旧表在一个数据库下，直接修改表名
						if (string.Compare(tbname[0], tboldname[0], true) == 0)
							sbalter.Append("ALTER TABLE ").Append(_commonUtils.QuoteSqlName($"{tboldname[0]}.{tboldname[1]}")).Append(" RENAME TO ").Append(_commonUtils.QuoteSqlName($"{tbname[0]}.{tbname[1]}")).Append(";\r\n");
						else {
							//如果新表，旧表不在一起，创建新表，导入数据，删除旧表
							istmpatler = true;
						}
					} else
						tboldname = null; //如果新表已经存在，不走改表名逻辑

					//对比字段，只可以修改类型、增加字段、有限的修改字段名；保证安全不删除字段
					var addcols = new Dictionary<string, ColumnInfo>(StringComparer.CurrentCultureIgnoreCase);
					foreach (var tbcol in tb.Columns) addcols.Add(tbcol.Value.Attribute.Name, tbcol.Value);
					var surplus = new Dictionary<string, bool>(StringComparer.CurrentCultureIgnoreCase);
					var dbcols = new List<DbColumnInfo>();
					var sql = @"select
a.column_name,
a.column_type,
case when a.is_nullable = 'YES' then 1 else 0 end 'is_nullable',
case when locate('auto_increment', a.extra) > 0 then 1 else 0 end 'is_identity'
from information_schema.columns a
where a.table_schema in ({0}) and a.table_name in ({1})".FormatMySql(tboldname ?? tbname);
					var ds = _orm.Ado.ExecuteArray(CommandType.Text, sql);
					var tbstruct = ds.ToDictionary(a => string.Concat(a[0]), a => new {
						column = string.Concat(a[0]),
						sqlType = string.Concat(a[1]),
						is_nullable = string.Concat(a[2]) == "1",
						is_identity = string.Concat(a[3]) == "1",
						is_unsigned = string.Concat(a[1]).EndsWith(" unsigned")
					}, StringComparer.CurrentCultureIgnoreCase);

					if (istmpatler == false) {
						foreach (var tbcol in tb.Columns.Values) {
							if (tbstruct.TryGetValue(tbcol.Attribute.Name, out var tbstructcol) ||
								string.IsNullOrEmpty(tbcol.Attribute.OldName) == false && tbstruct.TryGetValue(tbcol.Attribute.OldName, out tbstructcol)) {
								if ((tbcol.Attribute.DbType.IndexOf(" unsigned", StringComparison.CurrentCultureIgnoreCase) != -1) != tbstructcol.is_unsigned ||
								tbcol.Attribute.DbType.StartsWith(tbstructcol.sqlType, StringComparison.CurrentCultureIgnoreCase) == false ||
								tbcol.Attribute.IsNullable != tbstructcol.is_nullable ||
								tbcol.Attribute.IsIdentity != tbstructcol.is_identity) {
									sbalter.Append("ALTER TABLE ").Append(_commonUtils.QuoteSqlName($"{tbname[0]}.{tbname[1]}")).Append(" MODIFY ").Append(_commonUtils.QuoteSqlName(tbstructcol.column)).Append(" ").Append(tbcol.Attribute.DbType);
									if (tbcol.Attribute.IsIdentity && tbcol.Attribute.DbType.IndexOf("AUTO_INCREMENT", StringComparison.CurrentCultureIgnoreCase) == -1) sbalter.Append(" AUTO_INCREMENT");
									sbalter.Append(";\r\n");
								}
								if (tbstructcol.column == tbcol.Attribute.OldName) {
									//修改列名
									sbalter.Append("ALTER TABLE ").Append(_commonUtils.QuoteSqlName($"{tbname[0]}.{tbname[1]}")).Append(" CHANGE COLUMN ").Append(_commonUtils.QuoteSqlName(tbcol.Attribute.OldName)).Append(" ").Append(_commonUtils.QuoteSqlName(tbcol.Attribute.Name)).Append(" ").Append(tbcol.Attribute.DbType);
									if (tbcol.Attribute.IsIdentity && tbcol.Attribute.DbType.IndexOf("AUTO_INCREMENT", StringComparison.CurrentCultureIgnoreCase) == -1) sb.Append(" AUTO_INCREMENT");
									sbalter.Append(";\r\n");
								}
								continue;
							}
							//添加列
							sbalter.Append("ALTER TABLE ").Append(_commonUtils.QuoteSqlName($"{tbname[0]}.{tbname[1]}")).Append(" ADD ").Append(_commonUtils.QuoteSqlName(tbcol.Attribute.Name)).Append(" ").Append(tbcol.Attribute.DbType);
							if (tbcol.Attribute.IsIdentity && tbcol.Attribute.DbType.IndexOf("AUTO_INCREMENT", StringComparison.CurrentCultureIgnoreCase) == -1) sbalter.Append(" AUTO_INCREMENT");
							sbalter.Append(";\r\n");
						}
					}
					if (istmpatler == false) {
						sb.Append(sbalter);
						continue;
					}

					//创建临时表，数据导进临时表，然后删除原表，将临时表改名为原表名
					var tablename = tboldname == null ? _commonUtils.QuoteSqlName($"{tbname[0]}.{tbname[1]}") : _commonUtils.QuoteSqlName($"{tboldname[0]}.{tboldname[1]}");
					var tmptablename = _commonUtils.QuoteSqlName($"{tbname[0]}.FreeSqlTmp_{tbname[1]}");
					//创建临时表
					sb.Append("CREATE TABLE IF NOT EXISTS ").Append(tmptablename).Append(" (");
					foreach (var tbcol in tb.Columns.Values) {
						sb.Append(" \r\n  ").Append(_commonUtils.QuoteSqlName(tbcol.Attribute.Name)).Append(" ");
						sb.Append(tbcol.Attribute.DbType);
						if (tbcol.Attribute.IsIdentity && tbcol.Attribute.DbType.IndexOf("AUTO_INCREMENT", StringComparison.CurrentCultureIgnoreCase) == -1) sb.Append(" AUTO_INCREMENT");
						sb.Append(",");
					}
					if (tb.Primarys.Any() == false)
						sb.Remove(sb.Length - 1, 1);
					else {
						sb.Append(" \r\n  PRIMARY KEY (");
						foreach (var tbcol in tb.Primarys) sb.Append(_commonUtils.QuoteSqlName(tbcol.Attribute.Name)).Append(", ");
						sb.Remove(sb.Length - 2, 2).Append(")");
					}
					sb.Append("\r\n) Engine=InnoDB CHARACTER SET utf8;\r\n");
					sb.Append("INSERT INTO ").Append(tmptablename).Append(" (");
					foreach (var tbcol in tb.Columns.Values) {
						if (tbstruct.ContainsKey(tbcol.Attribute.Name) ||
							string.IsNullOrEmpty(tbcol.Attribute.OldName) == false && tbstruct.ContainsKey(tbcol.Attribute.OldName)) { //导入旧表存在的字段
							sb.Append(_commonUtils.QuoteSqlName(tbcol.Attribute.Name)).Append(", ");
						}
					}
					sb.Remove(sb.Length - 2, 2).Append(")\r\nSELECT ");
					foreach (var tbcol in tb.Columns.Values) {
						if (tbstruct.TryGetValue(tbcol.Attribute.Name, out var tbstructcol) ||
							string.IsNullOrEmpty(tbcol.Attribute.OldName) == false && tbstruct.TryGetValue(tbcol.Attribute.OldName, out tbstructcol)) {
							var insertvalue = _commonUtils.QuoteSqlName(tbstructcol.column);
							if (tbcol.Attribute.DbType.StartsWith(tbstructcol.sqlType, StringComparison.CurrentCultureIgnoreCase) == false) {
								//var tbcoldbtype = tbcol.Attribute.DbType.Split(' ').First();
								//insertvalue = $"cast({insertvalue} as {tbcoldbtype})";
							}
							if (tbcol.Attribute.IsNullable != tbstructcol.is_nullable) {
								insertvalue = $"ifnull({insertvalue},{_commonUtils.FormatSql("{0}", tbcol.Attribute.DbDefautValue).Replace("'", "''")})";
							}
							sb.Append(insertvalue).Append(", ");
						}
					}
					sb.Remove(sb.Length - 2, 2).Append(" FROM ").Append(tablename).Append(";\r\n");
					sb.Append("DROP TABLE ").Append(tablename).Append(";\r\n");
					sb.Append("ALTER TABLE ").Append(tmptablename).Append(" RENAME TO ").Append(_commonUtils.QuoteSqlName($"{tbname[0]}.{tbname[1]}")).Append(";\r\n");
				}
				return sb.Length == 0 ? null : sb.ToString();
			} finally {
				try {
					conn.Value.ChangeDatabase(database);
					_orm.Ado.MasterPool.Return(conn);
				} catch {
					_orm.Ado.MasterPool.Return(conn, true);
				}
			}
		}

		ConcurrentDictionary<string, bool> dicSyced = new ConcurrentDictionary<string, bool>();
		public bool SyncStructure<TEntity>() => this.SyncStructure(typeof(TEntity));
		public bool SyncStructure(params Type[] entityTypes) {
			if (entityTypes == null) return true;
			var syncTypes = entityTypes.Where(a => dicSyced.ContainsKey(a.FullName) == false).ToArray();
			if (syncTypes.Any() == false) return true;
			var ddl = this.GetComparisonDDLStatements(syncTypes);
			if (string.IsNullOrEmpty(ddl)) {
				foreach (var syncType in syncTypes) dicSyced.TryAdd(syncType.FullName, true);
				return true;
			}
			var affrows = _orm.Ado.ExecuteNonQuery(CommandType.Text, ddl);
			foreach (var syncType in syncTypes) dicSyced.TryAdd(syncType.FullName, true);
			return affrows > 0;
		}

	}
}