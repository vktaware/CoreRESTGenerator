using Microsoft.Data.SqlClient;
using Dapper;


namespace VickysMicroArc
{
    public class DatabaseSchemaReader
    {
        private readonly string _connectionString;

        public DatabaseSchemaReader(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IEnumerable<Table> GetTables()
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection.Query<Table>("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE'");
        }

        public IEnumerable<Column> GetColumns(string tableName)
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            var query = @"
            SELECT COLUMN_NAME, DATA_TYPE 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_NAME = @TableName";

            return connection.Query<Column>(query, new { TableName = tableName });
        }

        public string GetPrimaryKey(string tableName)
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            var query = @"
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE TABLE_NAME = @TableName
              AND OBJECTPROPERTY(OBJECT_ID(CONSTRAINT_SCHEMA + '.' + CONSTRAINT_NAME), 'IsPrimaryKey') = 1";

            return connection.QuerySingleOrDefault<string>(query, new { TableName = tableName });
        }
    }

    public class Table
    {
        public string TABLE_NAME { get; set; }
    }

    public class Column
    {
        public string COLUMN_NAME { get; set; }
        public string DATA_TYPE { get; set; }
    }


}

