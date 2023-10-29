using Abraham.ProgramSettingsManager;
using Abraham.HomenetBase.Connectors;
using Abraham.HomenetBase.Models;
using Abraham.MQTTClient;

namespace BackupServerDaemon
{
    class BackupMonitorLogic
    {
        #region ------------- Types ---------------------------------------------------------------
        public class Results
        {
            public bool Success { get; }
            public string ErrorMessages { get; }
            public string Rating { get; }
            public int Age { get; }
            public string FolderName { get; internal set; }

            public Results(bool success, string rating, int age, string folderName)
            {
                Success = success;
                Rating = rating;
                Age = age;
                FolderName = folderName;
            }

            public Results(bool success, Exception ex)
            {
                Success = success;
                ErrorMessages = ex.ToString();
            }

            public Results(bool success, string rating)
            {
                Success = success;
                Rating = rating;
                Age = 9999999;
                FolderName = "";
            }
        }
        #endregion



        #region ------------- Properties ---------------------------------------------------------
        public int UpdateIntervalInMinutes { get { return (_config is not null) ? _config.UpdateIntervalInMinutes : 1;} }
        public int MaxLogMessagesInUI { get { return (_config is not null) ? ((_config.MaxLogMessagesInUI < 1) ? 1 : _config.MaxLogMessagesInUI) : 1000; } }

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
            public string MqttServerURL { get; set; }
            public string MqttUsername { get; set; }
            public string MqttPassword { get; set; }
            public int ServerTimeout { get; set; }
            public int MaxLogMessagesInUI { get; set; }
            public int UpdateIntervalInMinutes { get; set; }
            public int TimezoneOffset { get; set; }
            public string BaseFolder { get; set; }  // this should be a container volume
            public List<Group> Groups { get; set; }

            public override string ToString()
            {
                return 
                    $"Home Automation Server  : {ServerURL} / {Username} / ***************\n" +
                    $"MQTT Broker             : {MqttServerURL} / {MqttUsername} / ***************\n" +
                    $"ServerTimeout           : {ServerTimeout}\n" +
                    $"MaxLogMessagesInUI      : {MaxLogMessagesInUI}\n" +
                    $"UpdateIntervalInMinutes : {UpdateIntervalInMinutes}\n" +
                    $"TimezoneOffset          : {TimezoneOffset} hours\n" +
                    $"BaseFolder              : {BaseFolder}\n" +
                    $"Groups:\n" + string.Join("\n", Groups);
            }
        }

        private class Group
        {
            public string DataObjectName { get; set; }
            public string MqttTopic { get; set; }
            public string Strategy { get; set; }
            public List<Folder> Folders { get; set; }
            public List<Rating> Ratings { get; set; }

            public override string ToString()
            {
                return $"    DataObject         : {DataObjectName}\n" +
                       $"    MQTT topic         : {MqttTopic}\n" +
                       $"    Strategy           : {Strategy}\n" +
                       $"    Folders:\n" + string.Join("\n", Folders) +
                       $"    Ratings:\n" + string.Join("\n", Ratings) +
                       $"    \n";
            }
        }

        private class Folder
        {
            public string Path { get; set; }
            public string IndicatorFile { get; set; }
            public string Strategy { get; set; }

            public override string ToString()
            {
                return $"        Path           : {Path}\n" +
                       $"        Strategy       : {Strategy}\n" +
                       $"        IndicatorFile  : {IndicatorFile}\n" + 
                       $"        \n";
            }
        }

        private class Rating
        {
            public int AgeDays { get; set; }
            public string Result { get; set; }

            public override string ToString()
            {
                return $"        age <= {AgeDays,9} days --> \"{Result}\"\n";
            }
        }

        private Configuration _config;
        private ProgramSettingsManager<Configuration> _configurationManager;
        
        // These file locations will be tried. The first file found will be used as configuration file
        private string[] _settingsFileOptions = new string[]
        {
            @"C:\Credentials\BackupServerDaemon\appsettings.hjson",
            "/opt/appsettings.hjson",
            "./appsettings.hjson",
        };
        
        // contains the filename of the configuration file that was actually chosen
        private string _configurationFilename = "";
        #endregion
        #region Server connections
        private static DataObjectsConnector _homenetClient;
        private static MQTTClient _mqttClient;
        #endregion
        #region private Types
        private class FileDetail
        {
            public string Name { get; }
            public DateTime Time { get; }

            public FileDetail(string name, DateTime time)
            {
                Name = name;
                Time = time;
            }
        }
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
            foreach(var option in _settingsFileOptions)
            {
                Logger($"Trying to read configuration from '{option}'...");
                if (File.Exists(option))
                    return ReadConfiguration_internal(option);
            }
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
            CheckAllGroups();
        }
        #endregion



        #region ------------- Implementation ------------------------------------------------------
        #region Monitoring
        private void CheckAllGroups()
        {
            try
            {
                CheckAllGroups_internal();
            }
            catch (Exception ex)
            {
                Logger($"Error reading groups: {ex}");
            }
        }

        private void CheckAllGroups_internal()
        {
            foreach (var group in _config.Groups)
            {
                Logger($"Group: {group.DataObjectName}");
                var result = CheckGroup(group);
                if (result.Success)
                    SendOutResults(result, group.DataObjectName, group.MqttTopic);
            }
        }

        private Results CheckGroup(Group group)
        {
            try
            {
                var groupResults = new List<Results>();

                foreach(var folder in group.Folders)
                    groupResults.Add(CheckFolder(folder, group.Ratings));

                var rating = RateGroupResults(group, groupResults);
                return new Results(true, rating);
            }
            catch (Exception ex)
            {
                Logger($"Error reading group: {ex}");
                return new Results(false, ex);
            }
        }

        private string RateGroupResults(Group group, List<Results> groupResults)
        {
            if (group.Strategy != "TakeNewestFolder" && 
                group.Strategy != "TakeOldestFolder")
            {
                Logger($"    Unknown strategy '{group.Strategy}'. Allowed values are 'TakeNewestFolder' and 'TakeOldestFolder'");
                return "rating error";
            }

            Logger($"Merging group results with strategy {group.Strategy}:");
            foreach(var result in groupResults)
            {
                Logger($"    {result.FolderName,-50} --> {result.Rating,-5} ({result.Age} days)");
                if (result.Rating == "rating error")
                    return "rating error";
            }

            var orderedResults = groupResults.OrderBy(x => x.Age);
            
            var totalRating = (group.Strategy == "TakeNewestFolder")
                ? orderedResults.Last().Rating
                : orderedResults.First().Rating;

            Logger($"Total rating: {totalRating}");
            return totalRating;
        }

        private Results CheckFolder(Folder folder, List<Rating> ratings)
        {
            try
            {
                return CheckFolder_internal(folder, ratings);
            }
            catch (Exception ex)
            {
                Logger($"Error reading folder: {ex}");
                return new Results(false, ex);
            }
        }

        private Results CheckFolder_internal(Folder folder, List<Rating> ratings)
        {
            var folderFullPath = Path.Combine(_config.BaseFolder, folder.Path);
            Logger($"    Reading folder  '{folderFullPath}' with mask '{folder.IndicatorFile}'");

            var fileNames = Directory.GetFiles(folderFullPath, folder.IndicatorFile, SearchOption.TopDirectoryOnly);
            if (fileNames.Count() == 0)
            {
                Logger($"    No files found.");
                return new Results(true, "no data");
            }

            var fileDetails = fileNames
                .Select(name => new FileDetail(name, File.GetLastWriteTime(name)))
                .ToList()
                .OrderBy(x => x.Time)
                .ToList();

            FileDetail pickedFile;
            TimeSpan age = TimeSpan.FromDays(9999999);
            if (folder.Strategy == "TakeNewestFileInRoot")
            {
                pickedFile = fileDetails.Last();
                age = DateTime.Now - pickedFile.Time;
            }
            else if (folder.Strategy == "TakeOldestFileInRoot")
            {
                pickedFile = fileDetails.First();
                age = DateTime.Now - pickedFile.Time;
            }
            else
            {
                Logger($"    Unknown strategy '{folder.Strategy}'. Allowed values are 'TakeNewestFileInRoot' and 'TakeOldestFileInRoot'");
                return new Results(true, "no data");
            }

            string rating = RateFileAge(age, ratings);

            // adjust displayed time to a hard coded timezone of necessary (if it isn't possible to set the docker container timezone)
            var displayedFileTime = pickedFile.Time.AddHours(_config.TimezoneOffset);

            Logger($"    Picked file:    '{pickedFile.Name,-80}' last write time: {displayedFileTime.ToString("yyyy-MM-dd HH:mm:ss")}. Age: {FormatAge(age),4}. Rating: {rating} ");
            return new Results(true, rating, age.Days, folder.Path);
        }

        private string FormatAge(TimeSpan age)
        {
            var result = "";

            if (age.Days > 0)
                result += $"{age.Days}d ";
            if (age.Hours > 0)
                result += $"{age.Hours}h ";
            if (age.Minutes > 0)
                result += $"{age.Minutes}m ";

            return result.Trim();
        }

        private string RateFileAge(TimeSpan age, List<Rating> ratings)
        {
            foreach(var rating in ratings)
            {
                if ((int)age.TotalDays <= rating.AgeDays)
                    return rating.Result;
            }   
            return "rating error";
        }
        #endregion
        #region Sending results
        private void SendOutResults(Results results, string dataObjectName, string mqttTopic)
        {
            if (HomenetServerIsConfigured())
            {
                if (!ConnectToHomenetServer())
                    Logger("Error connecting to homenet server.");
                else
                    UpdateDataObject(results, dataObjectName);
            }

            if (MqttBrokerIsConfigured())
            {
                if (!ConnectToMqttBroker())
                    Logger("Error connecting to MQTT broker.");
                else
                    UpdateTopic(results, mqttTopic);
            }
        }
        #endregion
        #region Home automation server connection
        private bool HomenetServerIsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_config.ServerURL) && 
                   !string.IsNullOrWhiteSpace(_config.Username) && 
                   !string.IsNullOrWhiteSpace(_config.Password);
        }

        private bool ConnectToHomenetServer()
        {
            Logger("Connecting to homenet server...");
            try
            {
                _homenetClient = new DataObjectsConnector(_config.ServerURL, _config.Username, _config.Password, _config.ServerTimeout);
                Logger("Connect successful");
                return true;
            }
            catch (Exception ex)
            {
                Logger("Error connecting to homenet server:\n" + ex.ToString());
                return false;
            }
        }

        private void UpdateDataObject(Results results, string dataObjectName)
        {
            if (_homenetClient is null)
                return;

            bool success = _homenetClient.UpdateValueOnly(new DataObject() { Name = dataObjectName, Value = results.Rating});
            if (success)
                Logger($"server updated");
            else
                Logger($"server update error! {_homenetClient.LastError}");
        }
        #endregion
        #region MQTT broker connection
        private bool MqttBrokerIsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_config.MqttServerURL) && 
                   !string.IsNullOrWhiteSpace(_config.MqttUsername) && 
                   !string.IsNullOrWhiteSpace(_config.MqttPassword);
        }

        private bool ConnectToMqttBroker()
        {
            Logger("Connecting to MQTT broker...");
            try
            {
                _mqttClient = new MQTTClient()
                    .UseUrl(_config.MqttServerURL)
                    .UseUsername(_config.MqttUsername)
                    .UsePassword(_config.MqttPassword)
                    .UseTimeout(_config.ServerTimeout)
                    .UseLogger(delegate(string message) { Logger(message); })
                    .Build();

                Logger("Created MQTT client");
                return true;
            }
            catch (Exception ex)
            {
                Logger("Error connecting to homenet server:\n" + ex.ToString());
                return false;
            }
        }

        private void UpdateTopic(Results results, string topicName)
        {
            if (_mqttClient is null || results is null)
                return;

            var result = _mqttClient.Publish(topicName, results.Rating);
            if (result.IsSuccess)
                Logger($"MQTT topic updated");
            else
                Logger($"MQTT topic update error! {result.ReasonString}");
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

                if (_config.MaxLogMessagesInUI < 1)
                    Logger($"Warning: Parameter '{nameof(_config.MaxLogMessagesInUI)}' is NOT set!");

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