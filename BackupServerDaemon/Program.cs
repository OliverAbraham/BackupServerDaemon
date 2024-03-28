using Abraham.Scheduler;

namespace BackupServerDaemon
{
    public class Program
    {
        #region ------------- Fields --------------------------------------------------------------
        private static BackupMonitorLogic _logic;
        private static Scheduler _scheduler;
        private static List<string> _log = new();
        #endregion



        #region ------------- Init ----------------------------------------------------------------
        public static void Main(string[] args)
        {
            Greeting();
            if (!InitBackupMonitor())
                return;
            Divider();
            if (!InitScheduler())
                return;
            InitAndStartWebServer(args);
        }
        #endregion



        #region ------------- Implementation ------------------------------------------------------
        private static void Greeting()
        {
            Log($"");
            Log($"");
            Log($"");
            Log($"---------------------------------------------------------------------------------------------------");
            Log($"Backup server monitoring daemon - Oliver Abraham - Version {AppVersion.Version.VERSION}");
            Log($"---------------------------------------------------------------------------------------------------");
        }

        private static void Divider()
        {
            Log($"---------------------------------------------------------------------------------------------------");
            Log($"");
            Log($"");
            Log($"");
        }

        private static bool InitBackupMonitor()
        {
            _logic = new BackupMonitorLogic();
            _logic.Logger = (message) => Log(message);
            var success = _logic.ReadConfiguration();
            if (success) _logic.LogConfiguration();
            return success;
        }

        private static bool InitScheduler()
        {
            if (_logic.UpdateIntervalInMinutes == 0)
            {
                Log("Doing a single monitoring run, then ending, because parameter 'UpdateIntervalInMinutes' is 0");
                _logic.Check();
                Log("ending.");
                return false;
            }
            else
            {
                Log("Starting periodic monitoring. Press Ctrl-C to stop.");

                _scheduler = new Scheduler()
                    .UseIntervalMinutes(_logic.UpdateIntervalInMinutes)
                    .UseAction(() => _logic.Check())
                    .Start();
                return true;
            }
        }

        private static void InitAndStartWebServer(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            // add Swagger
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            var app = builder.Build();

            app.MapGet("/", () => GetWholeLog());

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseAuthorization();
            //app.MapControllers();
            app.Run();
            _scheduler.Stop();
            Console.WriteLine("Program ended");
        }

        private static void Log(string message)
        {
            var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}: {message}";
            _log.Add(line);
            Console.WriteLine(line);

            // automatic purge
            int maxLines = (_logic is not null) ? _logic.MaxLogMessagesInUI : 100;
            while (_log.Count > maxLines)
                _log.RemoveAt(0);
        }

        private static string GetWholeLog()
        {
            return string.Join("\n", _log);
        }
        #endregion
    }
}
