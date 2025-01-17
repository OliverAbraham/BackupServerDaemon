using Abraham.ProgramSettingsManager;
using Abraham.HomenetBase.Connectors;
using Abraham.HomenetBase.Models;
using Abraham.MQTTClient;
using Abraham.Mail;
using System.Text;

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

            public string FormatForEmail()
            {
                if (Success)
                    return $"Group {FolderName,-40} has age {Age,2} days --> {Rating}";
                else
                    return $"Group {FolderName,-40} or a folder of it couldn't be read. Error messages: {ErrorMessages}";
            }
        }
        #endregion



        #region ------------- Properties ----------------------------------------------------------
        public int UpdateIntervalInMinutes { get { return (_config is not null) ? _config.UpdateIntervalInMinutes : 1;} }
        public int MaxLogMessagesInUI { get { return (_config is not null) ? ((_config.MaxLogMessagesInUI < 1) ? 1 : _config.MaxLogMessagesInUI) : 1000; } }

        public delegate void LoggerDelegate(string message);
        public LoggerDelegate Logger { get; set; }
        #endregion



        #region ------------- Configuration file --------------------------------------------------
        private class Configuration
        {
            public string       ServerURL               { get; set; }
            public string       Username                { get; set; }
            public string       Password                { get; set; }
            public string       MqttServerURL           { get; set; }
            public string       MqttUsername            { get; set; }
            public string       MqttPassword            { get; set; }
            public string       EmailHostname           { get; set; }
            public string       EmailUseSSL             { get; set; }
            public string       EmailSMTPPort           { get; set; }
            public string       EmailUsername           { get; set; }
            public string       EmailPassword           { get; set; }
            public string       EmailFrom               { get; set; }
            public string       EmailTo                 { get; set; }
            public string       EmailSubject            { get; set; }
            public int          ServerTimeout           { get; set; }
            public int          MaxLogMessagesInUI      { get; set; }
            public int          UpdateIntervalInMinutes { get; set; }
            public int          TimezoneOffset          { get; set; }
            public string       BaseFolder              { get; set; }  // this should be a container volume
            public List<Group>  Groups                  { get; set; }

            public override string ToString()
            {
                return 
                    $"Targets:\n" +
                    $"Home Automation target  : {ServerURL} / {Username} / ***************\n" +
                    $"MQTT broker target      : {MqttServerURL} / {MqttUsername} / ***************\n" +
                    $"Email target            : {EmailTo}\n" +
                    $" \n" +
                    $"Parameters:\n" +
                    $"ServerTimeout           : {ServerTimeout}\n" +
                    $"MaxLogMessagesInUI      : {MaxLogMessagesInUI}\n" +
                    $"UpdateIntervalInMinutes : {UpdateIntervalInMinutes}\n" +
                    $"TimezoneOffset          : {TimezoneOffset} hours\n" +
                    $" \n" +
                    $"Source directories:\n" +
                    $"BaseFolder              : {BaseFolder}\n" +
                    $"Groups:\n" +
                    string.Join("\n", Groups);
            }

            public bool EmailParametersAreSet()
            {
                return 
                !string.IsNullOrWhiteSpace(EmailHostname) &&
                !string.IsNullOrWhiteSpace(EmailUseSSL  ) &&
                !string.IsNullOrWhiteSpace(EmailSMTPPort) &&
                !string.IsNullOrWhiteSpace(EmailUsername) &&
                !string.IsNullOrWhiteSpace(EmailPassword) &&
                !string.IsNullOrWhiteSpace(EmailFrom    ) &&
                !string.IsNullOrWhiteSpace(EmailTo      ) &&
                !string.IsNullOrWhiteSpace(EmailSubject );
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
                return $"    GroupName/DataObject: {DataObjectName}\n" +
                       $"    MQTT topic          : {MqttTopic}\n" +
                       $"    Strategy            : {Strategy}\n" +
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
                return $"        Path            : {Path}\n" +
                       $"        Strategy        : {Strategy}\n" +
                       $"        IndicatorFile   : {IndicatorFile}\n" + 
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
        #endregion



        #region ------------- Fields --------------------------------------------------------------
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
        private static List<string> _logForNotificationEmail = new();

        #region Server connections
        private static DataObjectsConnector _homenetClient;
        private static MQTTClient _mqttClient;
        #endregion
        #endregion



        #region ------------- Private Types -------------------------------------------------------
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
                Log($"Trying to read configuration from '{option}'...");
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
            lines.ForEach(line => Log(line));
            Log("");
        }

        /// <summary>
        /// Runs the check and sends the results to the home automation server.
        /// </summary>
        public void Check()
        {
            ResetLogForEmail();
            CheckAllGroups();
        }
        #endregion



        #region ------------- Implementation ------------------------------------------------------
        #region Monitoring
        private void CheckAllGroups()
        {
            CheckAllGroups_internal();
        }

        private void CheckAllGroups_internal()
        {
            Log($"Analysis started.");

            var allGroupResults = new List<Results>();

            foreach (var group in _config.Groups)
            {
                try
                {
                    Log($"");
                    Log($"");
                    Log($"---------------------------------------- Group: {group.DataObjectName} ----------------------------------------");
                    Log($"Group: {group.DataObjectName}");
                    var result = CheckGroup(group);
                    if (result.Success)
                    {
                        SendOutResults(result, group.DataObjectName, group.MqttTopic);
                        allGroupResults.Add(result);
                    }
                }
                catch (Exception ex)
                {
                    Log($"CheckAllGroups_internal: Error processing directory group '{group.DataObjectName}': {ex}");
                }
            }
            Log($"All groups processed");

            SendOutEmail(allGroupResults);
            Log($"Analysis ended. Next in {_config.UpdateIntervalInMinutes} minutes.");
            Log($"");
        }

        private Results CheckGroup(Group group)
        {
            try
            {
                var groupResults = new List<Results>();

                foreach(var folder in group.Folders)
                    groupResults.Add(CheckFolder(folder, group.Ratings));

                var combinedResult = RateGroupResults(group, groupResults);
                return new Results(true, combinedResult.Rating, combinedResult.Age, group.DataObjectName);
            }
            catch (Exception ex)
            {
                Log($"Error reading group: {ex}");
                return new Results(false, ex);
            }
        }

        private Results RateGroupResults(Group group, List<Results> groupResults)
        {
            if (group.Strategy != "TakeNewestFolder" && 
                group.Strategy != "TakeOldestFolder")
            {
                Log($"    Unknown strategy '{group.Strategy}'. Allowed values are 'TakeNewestFolder' and 'TakeOldestFolder'");
                return new Results(false, "rating error");
            }

            Log($"");
            Log($"    Merging group results with strategy {group.Strategy}:");
            foreach(var result in groupResults)
            {
                Log($"    {result.FolderName,-50} --> {result.Rating,-5} ({result.Age} days)");
                if (result.Rating == "rating error")
                    return new Results(false, "rating error");
            }

            var orderedResults = groupResults.OrderBy(x => x.Age);
            
            var totalRating = (group.Strategy == "TakeNewestFolder")
                ? orderedResults.First()
                : orderedResults.Last();

            Log($"    Rating          : {totalRating.Age} days ----> {totalRating.Rating}");
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
                Log($"Error reading folder: {ex}");
                return new Results(false, ex.ToString(), 999999, folder.Path);
            }
        }

        private Results CheckFolder_internal(Folder folder, List<Rating> ratings)
        {
            var folderFullPath = Path.Combine(_config.BaseFolder, folder.Path);
            Log($"    Reading folder  : '{folderFullPath}' with mask '{folder.IndicatorFile}'");

            var fileNames = Directory.GetFiles(folderFullPath, folder.IndicatorFile, SearchOption.TopDirectoryOnly);
            if (fileNames.Count() == 0)
            {
                Log($"    No files found.");
                return new Results(true, "no data", 999999, folder.Path);
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
                Log($"    Unknown strategy '{folder.Strategy}'. Allowed values are 'TakeNewestFileInRoot' and 'TakeOldestFileInRoot'");
                return new Results(true, "no data", 999999, folder.Path);
            }

            string rating = RateFileAge(age, ratings);

            // adjust displayed time to a hard coded timezone of necessary (if it isn't possible to set the docker container timezone)
            var displayedFileTime = pickedFile.Time.AddHours(_config.TimezoneOffset);

            Log($"    Picked file     : '{pickedFile.Name,-80}'");
            Log($"    Last write time : {displayedFileTime.ToString("yyyy-MM-dd HH:mm:ss")}");
            Log($"    Rating          : Age: {age.TotalDays} days. -----> {rating}");
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
            SendOutToHomenet(results, dataObjectName);
            SendOutToMQTT(results, mqttTopic);
        }

        private void SendOutToHomenet(Results results, string dataObjectName)
        {
            try
            {
                if (HomenetServerIsConfigured())
                {
                    Log($"");
                    Log($"Sending out group result to Home automation target");
                    if (!ConnectToHomenetServer())
                        Log("Error connecting to homenet server.");
                    else
                        UpdateDataObject(results, dataObjectName);
                }
            }
            catch (Exception ex)
            {
                Log($"SendOutToHomenet: {ex}");
            }
        }

        private void SendOutToMQTT(Results results, string mqttTopic)
        {
            try
            {
                if (MqttBrokerIsConfigured())
                {
                    Log($"");
                    Log($"Sending out group result to MQTT target");
                    Log("Connecting to MQTT broker...");
                    if (!ConnectToMqttBroker())
                        Log("Error connecting to MQTT broker.");
                    else
                        UpdateTopics(results, mqttTopic);
                }
            }
            catch (Exception ex)
            {
                Log($"SendOutToMQTT: {ex}");
            }
        }
        #endregion

        #region Home automation server target
        private bool HomenetServerIsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_config.ServerURL) && 
                   !string.IsNullOrWhiteSpace(_config.Username) && 
                   !string.IsNullOrWhiteSpace(_config.Password);
        }

        private bool ConnectToHomenetServer()
        {
            Log("Connecting to homenet server...");
            try
            {
                _homenetClient = new DataObjectsConnector(_config.ServerURL, _config.Username, _config.Password, _config.ServerTimeout);
                Log("Connect successful");
                return true;
            }
            catch (Exception ex)
            {
                Log("Error connecting to homenet server:\n" + ex.ToString());
                return false;
            }
        }

        private void UpdateDataObject(Results results, string dataObjectName)
        {
            if (_homenetClient is null)
                return;

            bool success = _homenetClient.UpdateValueOnly(new DataObject() { Name = dataObjectName, Value = results.Rating});
            if (success)
                Log($"server updated");
            else
                Log($"server update error! {_homenetClient.LastError}");
        }
        #endregion

        #region MQTT target
        private bool MqttBrokerIsConfigured()
        {
            return !string.IsNullOrWhiteSpace(_config.MqttServerURL) && 
                   !string.IsNullOrWhiteSpace(_config.MqttUsername) && 
                   !string.IsNullOrWhiteSpace(_config.MqttPassword);
        }

        private bool ConnectToMqttBroker()
        {
            Log("Connecting to MQTT broker...");
            try
            {
                _mqttClient = new MQTTClient()
                    .UseUrl(_config.MqttServerURL)
                    .UseUsername(_config.MqttUsername)
                    .UsePassword(_config.MqttPassword)
                    .UseTimeout(_config.ServerTimeout)
                    .UseLogger(delegate(string message) { Log(message); })
                    .Build();

                Log("Created MQTT client");
                return true;
            }
            catch (Exception ex)
            {
                Log("Error connecting to MQTT broker:\n" + ex.ToString());
                return false;
            }
        }

        private void UpdateTopics(Results results, string topicName)
        {
            if (_mqttClient is null || results is null)
                return;

            var result = _mqttClient.Publish(topicName, results.Rating);
            if (result.IsSuccess)
                Log($"MQTT topic updated");
            else
                Log($"MQTT topic update error! {result.ReasonString}");
        }
        #endregion

        #region Email target
        private void SendOutEmail(List<Results> groupResults)
        {
            try
            {
                if (!_config.EmailParametersAreSet())
                {
                    Log("Email option is disabled. Not all Email parameters are set.");
                    return;
                }

                Log("Sending an email with the results...");

                if (!int.TryParse(_config.EmailSMTPPort, out int port))
                    throw new Exception($"The port number '{_config.EmailSMTPPort}' is not valid. Please check your settings!");

                var _client = new Abraham.Mail.SmtpClient()
                    .UseHostname(_config.EmailHostname)
                    .UsePort(port)
                    .UseAuthentication(_config.EmailUsername, _config.EmailPassword);

                if (_config.EmailUseSSL.ToLower() == "true")
                    _client.UseSecurityProtocol(Security.Ssl);

                Log("Connecting to the email host...");
                _client.Open();

                Log("Preparing the subject...");
                string subject = PrepareSubject(groupResults);
                Log($"---------> '{subject}'");

                Log("Preparing the body...");
                var body = PrepareBody(groupResults);

                Log("Sending...");

                _client.SendEmail(_config.EmailFrom, _config.EmailTo, subject, body);

                Log("Sending was successful.");

                _client.Close();
                Log("Connecting closed.");
            }
            catch (Exception ex)
            {
                Log("Problem sending an email!");
                Log(ex.ToString());
            }
        }

        private string PrepareSubject(List<Results> groupResults)
        {
            var subject = _config.EmailSubject;
            var oldestAgeOfAllGroups = groupResults.Where(x => !x.FolderName.Contains("BACKUP_EXTERN")).Max(x => x.Age);
            subject = subject.Replace("{{AGE}}", oldestAgeOfAllGroups.ToString());
            return subject;
        }

        private string PrepareBody(List<Results> groupResults)
        {
            var body = new StringBuilder();
            
            body.AppendLine("---------------------------------------------------------------------------------------------------------------------------------------------------");
            body.AppendLine("Summary:");
            body.AppendLine("---------------------------------------------------------------------------------------------------------------------------------------------------");

            foreach (var results in groupResults)
            {
                body.AppendLine(results.FormatForEmail());
            }

            body.AppendLine("");
            body.AppendLine("");
            body.AppendLine("---------------------------------------------------------------------------------------------------------------------------------------------------");
            body.AppendLine("Log:");
            body.AppendLine("---------------------------------------------------------------------------------------------------------------------------------------------------");
            _logForNotificationEmail.ForEach(m => body.AppendLine(m));

            return body.ToString();
        }
        #endregion

        #region Reading configuration file
        private bool ReadConfiguration_internal(string filename)
        {
            try
            {
                Log($"success");
                _configurationFilename = filename;

                _configurationManager = new ProgramSettingsManager<Configuration>()
                    .UseFullPathAndFilename(_configurationFilename)
                    .Load();

                _config = _configurationManager.Data;
                if (_config == null)
                {
                    Log($"No valid configuration found!\nExpecting file '{_configurationFilename}'");
                    return false;
                }

                if (_config.MaxLogMessagesInUI < 1)
                    Log($"Warning: Parameter '{nameof(_config.MaxLogMessagesInUI)}' is NOT set!");

                Log($"Configuration taken from file '{_configurationFilename}'");
                Log($"");
                Log($"");
                return true;
            }
            catch (Exception ex)
            {
                Log($"There was a problem reading the configuration file '{_configurationFilename}'");
                Log($"Please check the contents");
                Log($"More Info: {ex}");
                return false;
            }
        }

        private bool ReadConfiguration_error()
        {
            Log($"No configuration file found!");
            return false;
        }

        private void WriteConfiguration_internal()
        {
            _configurationManager.Save(_config);
        }
        #endregion

        #region InternalLogger
        private void Log(string message)
        {
            _logForNotificationEmail.Add(message);
            Logger(message);
        }
        
        private void ResetLogForEmail()
        {
            _logForNotificationEmail.Clear();
        }
        #endregion
        #endregion
    }
}