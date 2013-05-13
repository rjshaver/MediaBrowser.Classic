﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Code;
using MediaBrowser.Code.ShadowTypes;
using MediaBrowser.Library;
using MediaBrowser.Library.Configuration;
using MediaBrowser.Library.Logging;
using MediaBrowser.Library.Persistance;
using MediaBrowser.Library.Entities;
using MediaBrowser.Model.Updates;

namespace MediaBrowser
{

    [Serializable]
    public class CommonConfigData
    {
        [Group("Updates")]
        [Comment(@"Enable the automatic checking for updates (both MB and plugins).")]
        public bool EnableUpdates = true;
        [Group("Updates")]
        [Comment(@"The class of updates to check (Dev/Beta/Release).")]
        public PackageVersionClass SystemUpdateClass = PackageVersionClass.Dev;
        [Group("Updates")]
        [Comment(@"The class of updates to check (Dev/Beta/Release).")]
        public PackageVersionClass PluginUpdateClass = PackageVersionClass.Beta;
        [Hidden]
        public List<ExternalPlayer> ExternalPlayers = new List<ExternalPlayer>();
        [Dangerous]
        [Group("Advanced")]
        [Comment(@"The directory where MB was installed. Filled in at install time and used to call components.")]
        public string MBInstallDir = "";

        [Dangerous]
        [Comment(@"The version is used to determine if this is the first time a particular version has been run")]
        public string MBVersion = "1.0.0.0"; //default value will tell us if it is a brand new install

        [Dangerous]
        [Comment(@"Identifies if this is the very first time MB has been run.  Causes an initial setup routine to be performed.")]
        public bool IsFirstRun = true;

        public bool EnableMouseHook = false;

        public bool AutoValidate = true; //automatically validate and refresh items as we access them

        [Group("Display")]
        [Comment(@"The number of history items to show in the 'breadcrumbs' (how deep in the structure you are) .")]
        public int BreadcrumbCountLimit = 2;
        [Comment(@"The name of your top level item that will show in the 'breadcrumbs'.")]
        public string InitialBreadcrumbName = "Media";

        [Comment(@"Turns on logging for all MB components. Recommended you leave this on as it doesn't slow things down and is very helpful in troubleshooting.")]
        public bool EnableTraceLogging = true;
        public LogSeverity MinLoggingSeverity = LogSeverity.Debug;

        [Comment("The number of days to retain log files.  Files older than this will be deleted periodically")]
        public int LogFileRetentionDays = 30;

        [Dangerous]
        public List<string> PluginSources = new List<string>() { "http://www.mediabrowser.tv/plugins/multi/plugin_info.xml" };

        public enum ExternalPlayerLaunchType
        {
            CommandLine = 0,

            WMCNavigate = 1
        }

        public class ExternalPlayer
        {
            /// <summary>
            /// Determines if the external player can play multiple files without having to first generate a playlist
            /// </summary>
            public bool SupportsMultiFileCommandArguments { get; set; }

            /// <summary>
            /// Determines if playlist files are supported
            /// </summary>
            public bool SupportsPlaylists { get; set; }

            public ExternalPlayerLaunchType LaunchType { get; set; }
            public string ExternalPlayerName { get; set; }
            public List<MediaType> MediaTypes { get; set; }
            public List<VideoFormat> VideoFormats { get; set; }
            public string Command { get; set; }

            public string Args { get; set; }
            public bool MinimizeMCE { get; set; } //whether or not to minimize MCE when starting external player
            public bool ShowSplashScreen { get; set; } //whether or not to show the MB splash screen
            public bool HideTaskbar { get; set; }

            public ExternalPlayer()
            {
                MediaTypes = new List<MediaType>();

                foreach (MediaType val in Enum.GetValues(typeof(MediaType)))
                {
                    MediaTypes.Add(val);
                }

                VideoFormats = new List<VideoFormat>();

                foreach (VideoFormat val in Enum.GetValues(typeof(VideoFormat)))
                {
                    VideoFormats.Add(val);
                }
            }

            public string CommandFileName
            {
                get
                {
                    return string.IsNullOrEmpty(Command) ? string.Empty : Path.GetFileName(Command);
                }
            }

            public string MediaTypesFriendlyString
            {
                get
                {
                    if (MediaTypes.Count == Enum.GetNames(typeof(MediaType)).Count())
                    {
                        return "All";
                    }

                    return string.Join(",", MediaTypes.Select(i => i.ToString()).ToArray());
                }
            }
        }

        // for our reset routine
        public CommonConfigData ()
	    {
            try
            {
                File.Delete(ApplicationPaths.CommonConfigFile);
            }
            catch (Exception e)
            {
                Logger.ReportException("Unable to delete config file " + ApplicationPaths.CommonConfigFile, e);
            }
            //continue anyway
            this.file = ApplicationPaths.CommonConfigFile;
            this.settings = XmlSettings<CommonConfigData>.Bind(this, file);
	    }


        public CommonConfigData(string file)
        {
            this.file = file;
            this.settings = XmlSettings<CommonConfigData>.Bind(this, file);
        }

        [SkipField]
        string file;

        [SkipField]
        XmlSettings<CommonConfigData> settings;


        public static CommonConfigData FromFile(string file)
        {
            return new CommonConfigData(file);  
        }

        public void Save() {
            this.settings.Write();
            //notify of the change
            MediaBrowser.Library.Threading.Async.Queue("Config notify", () => Kernel.Instance.NotifyConfigChange());
        }

        /// <summary>
        /// Determines if a given external player configuration is configured to play a list of files
        /// </summary>
        public static bool CanPlay(CommonConfigData.ExternalPlayer player, IEnumerable<string> files)
        {
            IEnumerable<MediaType> types = files.Select(f => MediaTypeResolver.DetermineType(f));

            // See if there's a configured player matching the ExternalPlayerType and MediaType. 
            // We're not able to evaluate VideoFormat in this scenario
            // If none is found it will return null
            return CanPlay(player, types, new List<VideoFormat>(), files.Count() > 1);
        }

        /// <summary>
        /// Determines if a given external player configuration is configured to play a list of files
        /// </summary>
        public static bool CanPlay(CommonConfigData.ExternalPlayer player, IEnumerable<Media> mediaList)
        {
            var types = new List<MediaType>();
            var formats = new List<VideoFormat>();

            foreach (var media in mediaList)
            {
                var video = media as Video;

                if (video != null)
                {
                    if (!string.IsNullOrEmpty(video.VideoFormat))
                    {
                        var format = (VideoFormat)Enum.Parse(typeof(VideoFormat), video.VideoFormat);
                        formats.Add(format);
                    }

                }

                types.Add(media.MediaType);
            }

            bool isMultiFile = mediaList.Count() == 1 ? (mediaList.First().Files.Count() > 1) : (mediaList.Count() > 1);

            return CanPlay(player, types, formats, isMultiFile);
        }

        /// <summary>
        /// Detmines if a given external player configuration is configured to play:
        /// - ALL of MediaTypes supplied. This filter is ignored if an empty list is provided.
        /// - All of the VideoFormats supplied. This filter is ignored if an empty list is provided.
        /// - And is able to play the number of files requested
        /// </summary>
        public static bool CanPlay(CommonConfigData.ExternalPlayer externalPlayer, IEnumerable<MediaType> mediaTypes, IEnumerable<VideoFormat> videoFormats, bool isMultiFile)
        {
            // Check options to see if this is not a match
            if (Application.RunningOnExtender)
            {
                return false;
            }

            // If it's not even capable of playing multiple files in sequence, it's no good
            if (isMultiFile && !externalPlayer.SupportsMultiFileCommandArguments && !externalPlayer.SupportsPlaylists)
            {
                return false;
            }

            // If configuration wants specific MediaTypes, check that here
            // If no MediaTypes are specified, proceed
            foreach (MediaType mediaType in mediaTypes)
            {
                if (!externalPlayer.MediaTypes.Contains(mediaType))
                {
                    return false;
                }
            }

            // If configuration wants specific VideoFormats, check that here
            // If no VideoFormats are specified, proceed
            foreach (VideoFormat format in videoFormats)
            {
                if (!externalPlayer.VideoFormats.Contains(format))
                {
                    return false;
                }
            }

            return true;
        }

        
    }
}