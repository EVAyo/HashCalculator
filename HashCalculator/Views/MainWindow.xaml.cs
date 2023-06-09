﻿using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace HashCalculator
{
    public partial class MainWindow : Window
    {
        private readonly Basis VerificationBasis = new Basis();
        private readonly MainWndViewModel viewModel = new MainWndViewModel();

        public static MainWindow This { get; private set; }

        public static IntPtr WndHandle { get; private set; }

        public static ScrollViewer DataGridScroll { get; private set; }

        public MainWindow()
        {
            this.DataContext = this.viewModel;
            this.Loaded += this.MainWindowLoaded;
            this.InitializeComponent();
            this.Title = $"{Info.Title} v{Info.Ver} by {Info.Author} @ {Info.Published}";
            this.viewModel.ChangeTaskNumber(Settings.Current.SelectedTaskNumberLimit);
            This = this;
        }

        private void MainWindowLoaded(object sender, RoutedEventArgs e)
        {
            WndHandle = new WindowInteropHelper(this).Handle;
            if (VisualTreeHelper.GetChild(this.uiDataGrid_HashFiles, 0) is Border border)
            {
                DataGridScroll = border.Child as ScrollViewer;
            }
        }

        private void SearchUnderSpecifiedPolicy(IEnumerable<string> paths, List<string> outDataPaths)
        {
            switch (Settings.Current.SelectedDroppedSearchPolicy)
            {
                default:
                case SearchPolicy.Children:
                    foreach (string p in paths)
                    {
                        if (Directory.Exists(p))
                        {
                            DirectoryInfo di = new DirectoryInfo(p);
                            outDataPaths.AddRange(di.GetFiles().Select(i => i.FullName));
                        }
                        else if (File.Exists(p))
                        {
                            outDataPaths.Add(p);
                        }
                    }
                    break;
                case SearchPolicy.Descendants:
                    foreach (string p in paths)
                    {
                        if (Directory.Exists(p))
                        {
                            DirectoryInfo di = new DirectoryInfo(p);
                            outDataPaths.AddRange(
                                di.GetFiles("*", SearchOption.AllDirectories).Select(i => i.FullName)
                            );
                        }
                        else if (File.Exists(p))
                        {
                            outDataPaths.Add(p);
                        }
                    }
                    break;
                case SearchPolicy.DontSearch:
                    foreach (string p in paths)
                    {
                        if (File.Exists(p))
                        {
                            outDataPaths.Add(p);
                        }
                    }
                    break;
            }
        }

        private void DataGrid_FilesToCalculate_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] data) || data.Length == 0)
            {
                return;
            }
            this.AcceptNewFilePathsLockButtons();
            new Thread(() =>
            {
                List<string> searchedPaths = new List<string>();
                this.SearchUnderSpecifiedPolicy(data, searchedPaths);
                if (searchedPaths.Count == 0)
                {
                    return;
                }
                IEnumerable<ModelArg> modelArgs = searchedPaths.Select(s => new ModelArg(s));
                this.viewModel.DisplayHashViewModelsTask(modelArgs);
                this.AcceptNewFilePathsReleaseButtons();
            })
            { IsBackground = true }.Start();
        }

        private void Button_ClearFileList_Click(object sender, RoutedEventArgs e)
        {
            this.viewModel.ClearHashViewModels();
        }

        private void Button_ExportAsTextFile_Click(object sender, RoutedEventArgs e)
        {
            if (this.viewModel.HashViewModels.Count == 0)
            {
                MessageBox.Show("列表中没有任何需要导出的条目。", "提示");
                return;
            }
            SaveFileDialog sf = new SaveFileDialog()
            {
                ValidateNames = true,
                Filter = "文本文件|*.txt|所有文件|*.*",
                FileName = "hashsums.txt",
                InitialDirectory = Settings.Current.LastUsedPath,
            };
            if (sf.ShowDialog() != true)
            {
                return;
            }
            Settings.Current.LastUsedPath = Path.GetDirectoryName(sf.FileName);
            try
            {
                using (StreamWriter sw = File.CreateText(sf.FileName))
                {
                    foreach (HashViewModel hm in this.viewModel.HashViewModels)
                    {
                        if (hm.IsSucceeded && hm.Export)
                        {
                            sw.WriteLine($"{hm.Hash} *{hm.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"结果导出为文本文件失败：\n{ex.Message}", "错误");
                return;
            }
        }

        private void GlobalUpdateHashNameItems(string pathOrHash)
        {
            this.VerificationBasis.Clear();
            if (this.uiComboBox_ComparisonMethod.SelectedIndex == 0)
            {
                if (!File.Exists(pathOrHash))
                {
                    MessageBox.Show("校验依据来源文件不存在。", "提示");
                    return;
                }
                try
                {
                    foreach (string line in File.ReadAllLines(pathOrHash))
                    {
                        string[] items = line.Split(
                            new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (items.Length < 2)
                        {
                            if (MessageBox.Show(
                                    "哈希值文件行读取错误，可能该行格式不正确，是否继续？",
                                    "错误",
                                    MessageBoxButton.YesNo) == MessageBoxResult.No)
                            {
                                return;
                            }
                            else
                            {
                                continue;
                            }
                        }
                        this.VerificationBasis.Add(items);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"校验依据来源文件打开失败：\n{ex.Message}", "错误");
                    return;
                }
            }
            else
            {
                this.VerificationBasis.Add(new string[] { pathOrHash.Trim(), "" });
            }
        }

        private void CheckWithExpectedHash(string path)
        {
            FileInfo[] infosInSameFolder;
            string expectedHash;
            FileInfo hashValueFileInfo = new FileInfo(path);
            if (Settings.Current.SelectedQVSearchPolicy == SearchPolicy.Children)
            {
                infosInSameFolder = hashValueFileInfo.Directory.GetFiles();
            }
            else if (Settings.Current.SelectedQVSearchPolicy == SearchPolicy.Descendants)
            {
                infosInSameFolder = hashValueFileInfo.Directory.GetFiles(
                    "*", SearchOption.AllDirectories);
            }
            else
            {
                MessageBox.Show(
                    "设置项\"当使用快速校验时\"选择了不搜索文件夹，快速校验无法完成，请在设置面板选择合适选项。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            Dictionary<string, List<string>>
                nameFullName = new Dictionary<string, List<string>>();
            foreach (FileInfo info in infosInSameFolder)
            {
                string keylower = info.Name.ToLower();
                if (!nameFullName.ContainsKey(keylower))
                {
                    nameFullName[keylower] = new List<string> { info.FullName };
                }
                else
                {
                    nameFullName[keylower].Add(info.FullName);
                }
            }
            try
            {
                Dictionary<string, List<string>> verificationBasis
                    = new Dictionary<string, List<string>>();
                List<ModelArg> hashModelArgsList = new List<ModelArg>();
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] hashfname = line.Split(
                            new char[] { ' ' },
                            2,
                            StringSplitOptions.RemoveEmptyEntries);
                    if (hashfname.Length < 2)
                    {
                        if (MessageBox.Show(
                                "哈希值文件行读取错误，可能该行格式不正确，是否继续？",
                                "错误",
                                MessageBoxButton.YesNo) == MessageBoxResult.No)
                        {
                            return;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    hashfname[1] = hashfname[1].Trim(new char[] { '*', ' ', '\n' });
                    string namelower = hashfname[1].ToLower();
                    if (verificationBasis.ContainsKey(namelower))
                    {
                        verificationBasis[namelower].Add(hashfname[0]);
                    }
                    else
                    {
                        verificationBasis[namelower] = new List<string> { hashfname[0] };
                    }
                }
                foreach (KeyValuePair<string, List<string>> pair in verificationBasis)
                {
                    if (nameFullName.ContainsKey(pair.Key))
                    {
                        string first = pair.Value.First();
                        if (pair.Value.Count > 1)
                        {
                            expectedHash = pair.Value.All(s => s == first) ? first : "";
                        }
                        else
                        {
                            expectedHash = first;
                        }
                        foreach (string fullpath in nameFullName[pair.Key])
                        {
                            hashModelArgsList.Add(new ModelArg(expectedHash, fullpath));
                        }
                    }
                    else
                    {
                        hashModelArgsList.Add(new ModelArg(pair.Key, true));
                    }
                }
                if (hashModelArgsList.Count == 0)
                {
                    MessageBox.Show(
                        "校验依据内容为空或搜索到的文件数量为零，请检查设置面板中的\"当使用快速校验时\"选项。",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
                this.viewModel.DisplayHashViewModelsTask(hashModelArgsList);
            }
            catch { return; }
        }

        private void AcceptNewFilePathsLockButtons()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                this.uiButton_SelectFilesToHash.IsEnabled = false;
                this.uiDataGrid_HashFiles.AllowDrop = false;
                this.uiButton_SelectFoldersToHash.IsEnabled = false;
            });
        }

        private void AcceptNewFilePathsReleaseButtons()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                this.uiButton_SelectFilesToHash.IsEnabled = true;
                this.uiDataGrid_HashFiles.AllowDrop = true;
                this.uiButton_SelectFoldersToHash.IsEnabled = true;
            });
        }

        private void Button_StartCompare_Click(object sender, RoutedEventArgs e)
        {
            string pathOrHash = this.uiTextBox_HashValueOrFilePath.Text;
            if (string.IsNullOrEmpty(pathOrHash))
            {
                MessageBox.Show("未输入哈希值校验依据。", "提示");
                return;
            }
            if (this.viewModel.HashViewModels.Count == 0 && File.Exists(pathOrHash))
            {
                this.AcceptNewFilePathsLockButtons();
                new Thread(() =>
                {
                    this.CheckWithExpectedHash(pathOrHash);
                    this.AcceptNewFilePathsReleaseButtons();
                })
                { IsBackground = true }.Start();
            }
            else
            {
                this.GlobalUpdateHashNameItems(pathOrHash);
                foreach (HashViewModel hm in this.viewModel.HashViewModels)
                {
                    if (hm.IsSucceeded)
                    {
                        hm.CmpResult = this.VerificationBasis.Verify(hm.Name, hm.Hash);
                    }
                    else
                    {
                        hm.CmpResult = CmpRes.NoResult;
                    }
                }
                this.viewModel.GenerateVerificationReport();
            }
        }

        private void TextBox_HashValueOrFilePath_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        private void TextBox_HashValueOrFilePath_PreviewDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] data) || data.Length == 0)
            {
                return;
            }
            this.uiTextBox_HashValueOrFilePath.Text = data[0];
        }

        private void TextBox_HashValueOrFilePath_Changed(object sender, TextChangedEventArgs e)
        {
            this.uiComboBox_ComparisonMethod.SelectedIndex =
                File.Exists(this.uiTextBox_HashValueOrFilePath.Text) ? 0 : 1;
        }

        private void Button_CopyRefreshHash_Click(object sender, RoutedEventArgs e)
        {
            this.viewModel.Models_Restart(true, false);
        }

        private void Button_RefreshCurrentHash_Click(object sender, RoutedEventArgs e)
        {
            this.viewModel.Models_Restart(false, false);
        }

        private void Button_RefreshCurrentHashForce_Click(object sender, RoutedEventArgs e)
        {
            this.viewModel.Models_Restart(false, true);
        }

        private void MenuItem_Settings_Click(object sender, RoutedEventArgs e)
        {
            Window settingsWindow = new SettingsPanel() { Owner = this };
            settingsWindow.ShowDialog();
        }

        private void DataGrid_HashFiles_PrevKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.uiDataGrid_HashFiles.SelectedIndex = -1;
            }
        }

        private void Button_SelectHashSetFile_Click(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog openFile = new CommonOpenFileDialog
            {
                Title = "选择文件",
                InitialDirectory = Settings.Current.LastUsedPath,
            };
            if (openFile.ShowDialog() == CommonFileDialogResult.Ok)
            {
                Settings.Current.LastUsedPath = Path.GetDirectoryName(openFile.FileName);
                this.uiTextBox_HashValueOrFilePath.Text = openFile.FileName;
                // TextBox_HashValueOrFilePath_Changed 已实现
                //this.uiComboBox_ComparisonMethod.SelectedIndex = 0;
            }
        }

        private void MenuItem_UsingHelp_Click(object sender, RoutedEventArgs e)
        {
            Window usingHelpWindow = new UsingHelpWindow()
            {
                Owner = this,
            };
            usingHelpWindow.ShowDialog();
        }

        private void Button_CancelAllTask_Click(object sender, RoutedEventArgs e)
        {
            this.viewModel.Models_CancelAll();
        }

        private void Button_SelectFilesToHash_Click(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog fileOpen = new CommonOpenFileDialog
            {
                Title = "选择文件",
                InitialDirectory = Settings.Current.LastUsedPath,
                Multiselect = true,
                EnsureValidNames = true,
            };
            if (fileOpen.ShowDialog() == CommonFileDialogResult.Ok)
            {
                if (fileOpen.FileNames.Count() == 0)
                {
                    MessageBox.Show("没有选择任何文件", "提示");
                    return;
                }
                Settings.Current.LastUsedPath =
                    Path.GetDirectoryName(fileOpen.FileNames.ElementAt(0));
                this.AcceptNewFilePathsLockButtons();
                IEnumerable<ModelArg> modelArgs = fileOpen.FileNames.Select(s => new ModelArg(s));
                this.viewModel.DisplayHashViewModelsTask(modelArgs);
                this.AcceptNewFilePathsReleaseButtons();
            }
        }

        private void Button_SelectFoldersToHash_Click(object sender, RoutedEventArgs e)
        {
            if (Settings.Current.SelectedDroppedSearchPolicy == SearchPolicy.DontSearch)
            {
                MessageBox.Show(
                    "当前设置中的文件夹搜索策略为\"不搜索该文件夹\"，此按钮无法获取文件夹下的文件，请更改为其他选项。",
                    "提示");
                return;
            }
            CommonOpenFileDialog folderOpen = new CommonOpenFileDialog()
            {
                IsFolderPicker = true,
                InitialDirectory = Settings.Current.LastUsedPath,
                Multiselect = true,
                EnsureValidNames = true,
            };
            if (folderOpen.ShowDialog() == CommonFileDialogResult.Ok)
            {
                if (folderOpen.FileNames.Count() == 0)
                {
                    MessageBox.Show("没有选择任何文件夹", "提示");
                    return;
                }
                Settings.Current.LastUsedPath = folderOpen.FileNames.ElementAt(0);
                List<string> folderPaths = new List<string>();
                this.AcceptNewFilePathsLockButtons();
                new Thread(() =>
                {
                    this.SearchUnderSpecifiedPolicy(folderOpen.FileNames, folderPaths);
                    IEnumerable<ModelArg> modelArgs = folderPaths.Select(s => new ModelArg(s));
                    this.viewModel.DisplayHashViewModelsTask(modelArgs);
                    this.AcceptNewFilePathsReleaseButtons();
                })
                { IsBackground = true }.Start();
            }
        }

        private void Button_CancelHashModel_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            HashViewModel hashModel = button.DataContext as HashViewModel;
            this.viewModel.Models_CancelOne(hashModel);
        }

        private void Button_RestartHashModel_CLick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            HashViewModel hashModel = button.DataContext as HashViewModel;
            this.viewModel.Models_StartOne(hashModel);
        }

        private void Button_PauseHashModel_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            HashViewModel hashModel = button.DataContext as HashViewModel;
            this.viewModel.Models_PauseOne(hashModel);
        }

        private void Button_PauseAllTask_Click(object sender, RoutedEventArgs e)
        {
            this.viewModel.Models_PauseAll();
        }

        private void Button_ContinueAllTask_Click(object sender, RoutedEventArgs e)
        {
            this.viewModel.Models_ContinueAll();
        }

        private void Button_WindowTopmost_Click(object sender, RoutedEventArgs e)
        {
            Settings.Current.MainWndTopmost = !Settings.Current.MainWndTopmost;
        }
    }
}