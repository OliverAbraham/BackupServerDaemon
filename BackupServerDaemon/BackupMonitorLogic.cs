//using Abraham.HomenetBase.Connectors;
//using Abraham.HomenetBase.Models;
using Abraham.ProgramSettingsManager;
using System;
using System.Linq;

namespace BackupServerDaemon
{
    class BackupMonitorLogic
    {
        #region ------------- Types ---------------------------------------------------------------
        public class Results
        {
            public bool Success { get; }
            public string ErrorMessages { get; }
            public string Result { get; }

            public Results(bool success, string result)
            {
                Success = success;
                Result = result;
            }

            public Results(bool success, Exception ex)
            {
                Success = success;
                ErrorMessages = ex.ToString();
            }
        }
        #endregion



        #region ------------- Properties ---------------------------------------------------------
        public int UpdateIntervalInMinutes { get { return (_config is not null) ? _config.UpdateIntervalInMinutes : 1;} }
        public int MaxLogMessagesInUI { get { return (_config is not null) ? _config.MaxLogMessagesInUI : 1000; } }

        public delegate void LoggerDelegate(string message);
        public LoggerDelegate Logger { get; set; }
        #endregion



        #region ------------- Fields --------------------------------------------------------------
        #region Configuration
        private class Configuration
        {
            public string ServerURL { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
            public int MaxLogMessagesInUI { get; set; }
            public int UpdateIntervalInMinutes { get; set; }
            public string BaseFolder { get; set; }  // this should be a container volume
            public string Folder { get; set; }
            public string DataObject { get; set; }

            public override string ToString()
            {
                return 
                    $"ServerURL               : {ServerURL}\n" +
                    $"Username                : {Username}\n" +
                    $"Password                : ***************\n" +
                    $"MaxLogMessagesInUI      : {MaxLogMessagesInUI}\n" +
                    $"UpdateIntervalInMinutes : {UpdateIntervalInMinutes}\n" +
                    $"BaseFolder              : {BaseFolder} (this should be a docker volume mounted into the container, e.g. /mnt)\n" +
                    $"Folder                  : {Folder}\n" +
                    $"DataObject name         : {DataObject}\n";
            }
        }

        private Configuration _config;
        private ProgramSettingsManager<Configuration> _configurationManager;
        
        // if this file exists in the container, it will be used as configuration file
        private const string _configurationFilenameOption1 = "/opt/appsettings.hjson";
        
        // second try will be made with this file
        private const string _configurationFilenameOption2 = "./appsettings.hjson";
        
        // contains the filename of the configuration file that was actually used
        private string _configurationFilename = "";
        #endregion
        #region Home automation server connection
        //private static DataObjectsConnector _homenetClient;
        #endregion
        #endregion



        #region ------------- Ctor ----------------------------------------------------------------
        public BackupMonitorLogic()
        {
            Logger = (message) => {};
        }
        #endregion



        #region ------------- Methods -------------------------------------------------------------
        /// <summary>
        /// returns true if configuration was read successfully.
        /// </summary>
        public bool ReadConfiguration()
        {
            Logger($"Trying to read configuration from '{_configurationFilenameOption1}'...");
            if (File.Exists(_configurationFilenameOption1))
                return ReadConfiguration_internal(_configurationFilenameOption1);

            Logger($"Trying to read configuration from '{_configurationFilenameOption2}'...");
            if (File.Exists(_configurationFilenameOption2))
                return ReadConfiguration_internal(_configurationFilenameOption2);

            return ReadConfiguration_error();
        }

        /// <summary>
        /// prints the configuration to the log
        /// </summary>
        public void LogConfiguration()
        {
            var lines = _config.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            lines.ForEach(line => Logger(line));
            Logger("");
        }

        /// <summary>
        /// Runs the check and sends the results to the home automation server.
        /// </summary>
        public void Check()
        {
            try
            {
                var results = CheckAllFolders();

                if (results.Success)
                    SendOutResults(results);
            }
            catch (Exception ex)
            {
                Logger($"Error reading the folder '{_config.Folder}': {ex.ToString()}");
            }
        }
        #endregion



        #region ------------- Implementation ------------------------------------------------------
        #region Monitoring
        private Results CheckAllFolders()
        {
            var folder = Path.Combine(_config.BaseFolder, _config.Folder);
            try
            {
                Logger($"Reading folder '{folder}'");

                var files = Directory.GetFiles(folder);
                var creationTimes = files.Select(file => File.GetCreationTime(file)).ToList();
                var maxTime = creationTimes.Max();
                var age = DateTime.Now - maxTime;
                var result = InterpretAge(age);
                
                Logger($"The newest file has date/time {maxTime.ToString("yyyy-MM-dd HH:mm:ss")}. Age: {(int)age.TotalDays} days. Result: {result}");
                return new Results(true, result);
            }
            catch (Exception ex)
            {
                Logger($"Error reading folder '{folder}': {ex}");
                return new Results(false, ex);
            }
        }

        private string InterpretAge(TimeSpan age)
        {
            return age.TotalDays switch
            {
                <  1 => "OK",
                <= 1 => "1d",
                <= 2 => "2d",
                <= 3 => "3d",
                <= 4 => "4d",
                <= 5 => "5d",
                <= 6 => "6d",
                <= 7 => "1w",
                <= 14 => "2w",
                <= 21 => "3w",
                <= 28 => "4w",
                _ => "old"
            };
        }
        #endregion
        #region Sending results
        private void SendOutResults(Results results)
        {
            if (true)
            {
                if (!ConnectToHomenetServer())
                    Logger("Error connecting to homenet server.");
                else
                    SendStatusToServer(results);
                return;
            }
        }

        private static bool ConnectToHomenetServer()
        {
            //Log("Connecting to homenet server...");
            //try
            //{
            //    _homenetClient = new DataObjectsConnector(_logic.ServerURL, _logic.Username, _logic.Password, 30);
            //    Log("Connect successful");
                return true;
            //}
            //catch (Exception ex)
            //{
            //    Log("Error connecting to homenet server:\n" + ex.ToString());
            //    return false;
            //}
        }

        private static void SendStatusToServer(Results results)
        {
            //bool success = _homenetClient.UpdateValueOnly(new DataObject() { Name = dataObjectName, Value = dataObjectvalue });
            //Log($"{(success ? "ok" : "send error!")}");
        }
        #endregion
        #region Reading configuration file
        private bool ReadConfiguration_internal(string filename)
        {
            try
            {
                Logger($"success");
                _configurationFilename = filename;

                _configurationManager = new ProgramSettingsManager<Configuration>()
                    .UseFullPathAndFilename(_configurationFilename)
                    .Load();

                _config = _configurationManager.Data;
                if (_config == null)
                {
                    Logger($"No valid configuration found!\nExpecting file '{_configurationFilename}'");
                    return false;
                }
                Logger($"Configuration read from filename '{_configurationFilename}'");
                return true;
            }
            catch (Exception ex)
            {
                Logger($"There was a problem reading the configuration file '{_configurationFilename}'");
                Logger($"Please check the contents");
                Logger($"More Info: {ex}");
                return false;
            }
        }

        private bool ReadConfiguration_error()
        {
            Logger($"No configuration file found!");
            return false;
        }

        private void WriteConfiguration_internal()
        {
            _configurationManager.Save(_config);
        }
        #endregion
        #endregion
    }
}