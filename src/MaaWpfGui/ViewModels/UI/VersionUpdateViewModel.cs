// <copyright file="VersionUpdateViewModel.cs" company="MaaAssistantArknights">
// MaaWpfGui - A part of the MaaCoreArknights project
// Copyright (C) 2021 MistEO and Contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY
// </copyright>

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MaaWpfGui.Constants;
using MaaWpfGui.Helper;
using MaaWpfGui.Services.Updates;
using Markdig;
using Markdig.Wpf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Semver;
using Stylet;

namespace MaaWpfGui.ViewModels.UI
{
    /// <summary>
    /// The view model of version update.
    /// </summary>
    public class VersionUpdateViewModel : Screen
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VersionUpdateViewModel"/> class.
        /// </summary>
        public VersionUpdateViewModel()
        {
        }

        ~VersionUpdateViewModel()
        {
            UnbindUpdateService();
        }

        private static string AddContributorLink(string text)
        {
            /*
            //        "@ " -> "@ "
            //       "`@`" -> "`@`"
            //   "@MistEO" -> "[@MistEO](https://github.com/MistEO)"
            // "[@MistEO]" -> "[@MistEO]"
            */
            return Regex.Replace(text, @"([^\[`]|^)@([^\s]+)", "$1[@$2](https://github.com/$2)");
        }

        public string CurrentVersion => Instances.UpdateService.Version;

        public string Title { get; private set; }

        public string Description { get; private set; }

        public FlowDocument ReleaseNotesDoc { get; private set; }

        public UpdateService UpdateService { get; private set; }

        public bool IsReleaseNoteMode { get; private set; }

        public bool IsDownloadControlVisible { get; private set; }

        public bool IsProgressControlVisible { get; private set; }

        public bool IsRestartControlVisible { get; private set; }

        public string DownloadProgressText { get; private set; } = "114 MiB / 514 MiB @ 810 KiB/s";

        private static FlowDocument RenderMarkdown(string md)
        {
            var doc = Markdig.Wpf.Markdown.ToFlowDocument(AddContributorLink(md),
                new MarkdownPipelineBuilder().UseSupportedExtensions().Build());
            doc.Language = XmlLanguage.GetLanguage(Thread.CurrentThread.CurrentCulture.Name);
            doc.IsOptimalParagraphEnabled = true;
            return doc;
        }

        /// <summary>
        /// 如果是在更新后第一次启动，显示ReleaseNote弹窗，否则检查更新并下载更新包。
        /// </summary>
        public static void ShowReleaseNotesForMigration()
        {
            if (Convert.ToBoolean(ConfigurationHelper.GetValue(ConfigurationKeys.VersionUpdateIsFirstBoot, bool.FalseString)))
            {
                ConfigurationHelper.DeleteValue(ConfigurationKeys.VersionUpdateIsFirstBoot);
                var vm = new VersionUpdateViewModel();
                vm.ShowReleaseNotes();
            }
        }

        public void ShowReleaseNotes()
        {
            ReleaseNotesDoc = RenderMarkdown(ConfigurationHelper.GetValue(ConfigurationKeys.VersionUpdateBody, string.Empty));
            RenderUpdateStatus(UpdateStatus.None);
            Instances.WindowManager.ShowWindow(this);
        }

        private void BindUpdateService()
        {
            UpdateService = Instances.UpdateService;
            UpdateService.PropertyChanged += UpdateService_PropertyChanged;
        }

        private void UnbindUpdateService()
        {
            if (UpdateService != null)
            {
                UpdateService.PropertyChanged -= UpdateService_PropertyChanged;
                UpdateService = null;
            }
        }

        private void UpdateService_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UpdateService.UpdateStatus))
            {
                RenderUpdateStatus(UpdateService.UpdateStatus);
            }
        }

        private void RenderUpdateStatus(UpdateStatus status)
        {
            switch (status)
            {
                case UpdateStatus.Available:
                    IsDownloadControlVisible = true;
                    IsProgressControlVisible = false;
                    IsRestartControlVisible = false;
                    IsReleaseNoteMode = false;
                    Title = LocalizationHelper.GetString("UpdateAvailableTitle");
                    Description = string.Format(LocalizationHelper.GetString("UpdateAvailableDescription`2"), UpdateService?.UpdateInfo?.Tag ?? "UNKNOWN VERSION", CurrentVersion);
                    break;
                case UpdateStatus.Downloading:
                    IsDownloadControlVisible = false;
                    IsProgressControlVisible = true;
                    IsRestartControlVisible = false;
                    IsReleaseNoteMode = false;
                    Title = LocalizationHelper.GetString("UpdateDownloadingTitle");
                    Description = string.Format(LocalizationHelper.GetString("UpdateDownloadingDescription`2"), UpdateService?.UpdateInfo?.Tag ?? "UNKNOWN VERSION", CurrentVersion);
                    break;
                case UpdateStatus.Downloaded:
                    IsDownloadControlVisible = false;
                    IsProgressControlVisible = false;
                    IsRestartControlVisible = true;
                    IsReleaseNoteMode = false;
                    Title = LocalizationHelper.GetString("UpdatePendingTitle");
                    Description = string.Format(LocalizationHelper.GetString("UpdatePendingDescription`2"), UpdateService?.UpdateInfo?.Tag ?? "UNKNOWN VERSION", CurrentVersion);
                    break;
                case UpdateStatus.DownloadError:
                    IsDownloadControlVisible = false;
                    IsProgressControlVisible = true;
                    IsRestartControlVisible = false;
                    IsReleaseNoteMode = false;
                    break;
                default:
                    IsDownloadControlVisible = false;
                    IsProgressControlVisible = false;
                    IsRestartControlVisible = false;
                    IsReleaseNoteMode = true;
                    Title = LocalizationHelper.GetString("UpdateCompleteTitle");
                    Description = string.Format(LocalizationHelper.GetString("UpdateCompleteDescription`1"), CurrentVersion);
                    break;
            }
        }

        public void ShowUpdateInfo(UpdateInfo updateInfo)
        {
            BindUpdateService();
            ReleaseNotesDoc = RenderMarkdown(updateInfo.ReleaseNotes);
        }

        public static void PerformBackgroundCheck()
        {

        }

        public static void RestartAndUpdate()
        {
            var window = Application.Current.MainWindow;
            var closed = false;

            void onClosed(object sender, EventArgs e)
            {
                closed = true;
            }

            window.Closed += onClosed;
            window.Activate();
            window.Close(); // may have cancelled
            window.Closed -= onClosed;

            if (closed)
            {
                Application.Current.Shutdown();
                System.Windows.Forms.Application.Restart();
            }
        }

        static int debug_state = 0;

        public void ToggleState()
        {
            debug_state++;
            debug_state %= 6;
            RenderUpdateStatus((UpdateStatus)debug_state);
        }

        public void Close()
        {
            RequestClose();
        }

        private static ObservableCollection<LogItemViewModel> _logItemViewModels = null;

        public static void OutputDownloadProgress(long value = 0, long maximum = 1, int len = 0, double ts = 1)
        {
            OutputDownloadProgress(
                string.Format("[{0:F}MiB/{1:F}MiB({2}%) {3:F} KiB/s]",
                    value / 1048576.0,
                    maximum / 1048576.0,
                    100 * value / maximum,
                    len / ts / 1024.0));
        }

        private static void OutputDownloadProgress(string output, bool downloading = true)
        {
            if (_logItemViewModels == null)
            {
                return;
            }

            var log = new LogItemViewModel(downloading ? LocalizationHelper.GetString("NewVersionFoundDescDownloading") + "\n" + output : output, UiLogColor.Download);

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_logItemViewModels.Count > 0 && _logItemViewModels[0].Color == UiLogColor.Download)
                {
                    if (!string.IsNullOrEmpty(output))
                    {
                        _logItemViewModels[0] = log;
                    }
                    else
                    {
                        _logItemViewModels.RemoveAt(0);
                    }
                }
                else if (!string.IsNullOrEmpty(output))
                {
                    _logItemViewModels.Clear();
                    _logItemViewModels.Add(log);
                }
            });
        }

        /// <summary>
        /// The event handler of opening hyperlink.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The event arguments.</param>
        public void OpenHyperlink(object sender, ExecutedRoutedEventArgs e)
        {
            Process.Start(e.Parameter.ToString());
        }
    }
}
