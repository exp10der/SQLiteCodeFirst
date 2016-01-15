using System;
using System.Data.Entity;
using System.Data.Entity.ModelConfiguration.Conventions;
using SQLite.CodeFirst.Conventions;
using System.IO;

namespace SQLite.CodeFirst
{
    /// <summary>
    /// An basic implementation of the <see cref="IDatabaseInitializer{TContext}"/> interface.
    /// This class provides common logic which can be used when writing an Sqlite-Initializer.
    /// The logic provided is: 
    ///   1. Remove/Add specific Conventions 
    ///   2. Get the path to the database file  
    ///   3. Create a new SQLite-Database from the model (Code First)
    ///   4. Seed data to the new created database
    /// The following implementations are provided: <see cref="T:SQLite.CodeFirst.SqliteCreateDatabaseIfNotExists`1"/>,  <see cref="T:SQlite.CodeFirst.SqliteDropCreateDatabaseAlways`1"/>.
    /// </summary>
    /// <typeparam name="TContext">The type of the context.</typeparam>
    public abstract class SqliteInitializerBase<TContext> : IDatabaseInitializer<TContext>
        where TContext : DbContext
    {
        private readonly DbModelBuilder modelBuilder;

        protected SqliteInitializerBase(DbModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
            {
                throw new ArgumentNullException("modelBuilder");
            }

            this.modelBuilder = modelBuilder;

            // This convention will crash the SQLite Provider before "InitializeDatabase" gets called.
            // See https://github.com/msallin/SQLiteCodeFirst/issues/7 for details.
            modelBuilder.Conventions.Remove<TimestampAttributeConvention>();

            // By default there is a 'ForeignKeyIndexConvention' but it can be removed.
            // And there is no "Contains" and no way to enumerate the ConventionsCollection.
            // So a try/catch will do the job.
            try
            {
                // Place the own ForeinKeyIndexConvention right after the original.
                // The own convention will rename the automatically created indicies by using the correct scheme.
                modelBuilder.Conventions.AddAfter<ForeignKeyIndexConvention>(new SqliteForeignKeyIndexConvention());
            }
            catch (InvalidOperationException exception)
            {
                // Ignore it.
            }

            // The Entity Framework does not support the "UNIQUE" Keyword.
            // Thus this is added by using a custom Convention and a custom Attribute (UniqueAttribute)
            modelBuilder.Conventions.Add<UniqueConvention>();
        }

        /// <summary>
        /// Initialize the database for the given context.
        /// Generates the SQLite-DDL from the model and executs it against the database.
        /// After that the <see cref="Seed"/> method is executed.
        /// All actions are be executed in transactions.
        /// </summary>
        /// <param name="context">The context. </param>
        public virtual void InitializeDatabase(TContext context)
        {
            var model = modelBuilder.Build(context.Database.Connection);

            var dbFile = GetDatabasePathFromContext(context);
            var dbFileInfo = new FileInfo(dbFile);
            dbFileInfo.Directory.Create();

            using (var transaction = context.Database.BeginTransaction())
            {
                try
                {
                    var sqliteDatabaseCreator = new SqliteDatabaseCreator(context.Database, model);
                    sqliteDatabaseCreator.Create();
                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
            }

            using (var transaction = context.Database.BeginTransaction())
            {
                try
                {
                    Seed(context);
                    context.SaveChanges();
                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        /// <summary>
        /// Is executed right after the initialization <seealso cref="InitializeDatabase"/>.
        /// Use this method to seed data into the empty database.
        /// </summary>
        /// <param name="context">The context.</param>
        protected virtual void Seed(TContext context) { }

        /// <summary>
        /// Gets the database path file path from a <see cref="TContext"/>.
        /// </summary>
        /// <param name="context">The context to get the database file path from.</param>
        /// <returns>The full path to the SQLite database file.</returns>
        protected string GetDatabasePathFromContext(TContext context)
        {
            return SqliteConnectionStringParser.GetDataSource(context.Database.Connection.ConnectionString);
        }
    }
}