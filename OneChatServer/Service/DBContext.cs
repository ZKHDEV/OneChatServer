using OneChatServer.Model;
using SQLite.Net;
using SQLite.Net.Platform.WinRT;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace OneChatServer.Service
{
    public class DBContext
    {
        private const string DBName = "OneChatServerDB.db";
        private static string DBPath;

        public static void InitDB()
        {
            DBPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, DBName);

            using (var conn = GetSQLConnection())
            {
                conn.CreateTable<db_Message>();
            }
        }

        public static SQLiteConnection GetSQLConnection()
        {
            var con = new SQLiteConnection(new SQLitePlatformWinRT(), DBPath);

            return con;
        }
    }
}
