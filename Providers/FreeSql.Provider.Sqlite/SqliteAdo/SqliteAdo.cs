﻿using FreeSql.Internal;
using FreeSql.Internal.Model;
using SafeObjectPool;
using System;
using System.Collections;
using System.Data.Common;
using System.Text;
using System.Threading;

namespace FreeSql.Sqlite
{
    class SqliteAdo : FreeSql.Internal.CommonProvider.AdoProvider
    {
        public SqliteAdo() : base(DataType.Sqlite) { }
        public SqliteAdo(CommonUtils util, string masterConnectionString, string[] slaveConnectionStrings, Func<DbConnection> connectionFactory) : base(DataType.Sqlite)
        {
            base._util = util;
            if (connectionFactory != null)
            {
                MasterPool = new FreeSql.Internal.CommonProvider.DbConnectionPool(DataType.Sqlite, connectionFactory);
                return;
            }
            if (!string.IsNullOrEmpty(masterConnectionString))
                MasterPool = new SqliteConnectionPool("主库", masterConnectionString, null, null);
            if (slaveConnectionStrings != null)
            {
                foreach (var slaveConnectionString in slaveConnectionStrings)
                {
                    var slavePool = new SqliteConnectionPool($"从库{SlavePools.Count + 1}", slaveConnectionString, () => Interlocked.Decrement(ref slaveUnavailables), () => Interlocked.Increment(ref slaveUnavailables));
                    SlavePools.Add(slavePool);
                }
            }
        }
        public override object AddslashesProcessParam(object param, Type mapType, ColumnInfo mapColumn)
        {
            if (param == null) return "NULL";
            if (mapType != null && mapType != param.GetType() && (param is IEnumerable == false || mapType.IsArrayOrList()))
                param = Utils.GetDataReaderValue(mapType, param);
            if (param is bool || param is bool?)
                return (bool)param ? 1 : 0;
            else if (param is string || param is char)
                return string.Concat("'", param.ToString().Replace("'", "''"), "'");
            else if (param is Enum)
                return ((Enum)param).ToInt64();
            else if (decimal.TryParse(string.Concat(param), out var trydec))
                return param;
            else if (param is DateTime || param is DateTime?)
                return string.Concat("'", ((DateTime)param).ToString("yyyy-MM-dd HH:mm:ss"), "'");
            else if (param is TimeSpan || param is TimeSpan?)
                return ((TimeSpan)param).Ticks / 10000;
            else if (param is IEnumerable)
                return AddslashesIEnumerable(param, mapType, mapColumn);

            return string.Concat("'", param.ToString().Replace("'", "''"), "'");
        }

        protected override DbCommand CreateCommand()
        {
            return AdonetPortable.GetSqliteCommand();
        }

        protected override void ReturnConnection(IObjectPool<DbConnection> pool, Object<DbConnection> conn, Exception ex)
        {
            var rawPool = pool as SqliteConnectionPool;
            if (rawPool != null) rawPool.Return(conn, ex);
            else pool.Return(conn);
        }

        protected override DbParameter[] GetDbParamtersByObject(string sql, object obj) => _util.GetDbParamtersByObject(sql, obj);
    }
}
