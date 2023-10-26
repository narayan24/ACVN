using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.Drawing;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Diagnostics;
using Microsoft.VisualBasic.Logging;
using System.Text.RegularExpressions;
using System.ComponentModel.Design;

namespace ACVN
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string storyPath;
        private string roomsPath;
        private string imagesPath;

        private string currentRoom;
        private string currentAction;

        public MainWindow()
        {
            InitializeComponent();

            string rootPath = AppDomain.CurrentDomain.BaseDirectory;
            Directory.SetCurrentDirectory(rootPath);

            storyPath = System.IO.Path.Combine(rootPath, "../../../story/");
            roomsPath = System.IO.Path.Combine(storyPath, "rooms");
            imagesPath = System.IO.Path.Combine(storyPath, "images");
            CheckFolder(storyPath);
            CheckFolder(roomsPath);
            CheckFolder(imagesPath);

            currentRoom = "start";
            currentAction = "start";

            InitContent();
        }

        private void CheckFolder(string folder)
        {
            if (!Directory.Exists(folder))
            {
                MessageBox.Show("The folder '"+folder+"' wasn't found");
            }
        }

        private string clearPath(string path)
        {
            return Regex.Replace(path, @"_", "/");
        }

        /* CONTENT HANDLING */
        private void InitContent()
        {
            string filePath = System.IO.Path.Combine(roomsPath, clearPath(currentRoom) + ".acvn");
            Debug.WriteLine(filePath);
            if (File.Exists(filePath) && System.IO.Path.GetExtension(filePath).Equals(".acvn", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string fileContent = File.ReadAllText(filePath);

                    string pattern = @"#begin\s+(.*?)\s+(.*?)#end";
                    MatchCollection matches = Regex.Matches(fileContent, pattern, RegexOptions.Singleline);
                    
                    if (matches.Count > 0)
                    {
                        foreach (Match match in matches)
                        {
                            if (match.Success)
                            {
                                string blockName = match.Groups[1].Value;
                                string blockContent = match.Groups[2].Value;

                                if (blockName == currentAction)
                                {
                                    ExtractActions(blockContent);
                                    ShowRandomMedia("rooms/" + clearPath(currentRoom) + "/" + currentAction);
                                }

                                fileContent = fileContent.Replace(match.Value, string.Empty);
                            }
                        }
                    } else
                    {
                        mainText.Text = fileContent;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error while reading the file: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show($"Error loading file '{filePath}'");
            }
        }
    

        private void ExtractActions(string content)
        {
            string[] commands = content.Split(new string[] { "[[", "]]" }, StringSplitOptions.RemoveEmptyEntries);
            if (commands.Length > 0)
            {
                leftStackPanel.Children.Clear();
                foreach (string command in commands)
                {
                    string[] commandParts = command.Split(',');

                    if (commandParts.Length < 2)
                        continue;

                    string text = commandParts[0].Trim();
                    string roomName = commandParts[1].Trim();

                    Button button = new Button
                    {
                        Content = text,
                        Margin = new Thickness(5),
                        Background = System.Windows.Media.Brushes.LightGray
                    };

                    button.Click += (sender, e) =>
                    {
                        ExecuteCommand(commandParts);
                    };

                    leftStackPanel.Children.Add(button);

                    string contentClean = Regex.Replace(content, @"#begin.*\n|#end\n*", string.Empty);
                    mainText.Text = Regex.Replace(contentClean, @"\[\[.*?\]\]", string.Empty).Trim();

                }
            }
        }

        private void ExecuteCommand(string[] command)
        {
            this.currentRoom = command[1].Trim();
            if (command.Length > 2)
            {
                this.currentAction = command[2].Trim();
            } else
            {
                currentAction = "start";
            }
            InitContent();
        }

        /* MEDIA HANDLING */
        private void ShowRandomMedia(string pathToSearch)
        {
            string path = imagesPath + "/" + pathToSearch;

            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*.*");

                Random random = new Random();
                int randomImage = random.Next(0, files.Length);
                DisplayMedia(files[randomImage]);
            } else
            {
                mainMedia.Visibility = Visibility.Collapsed;
            }
        }

        private void DisplayMedia(string file)
        {
            mainMedia.Visibility = Visibility.Visible;
            mainMedia.Source = new Uri(file);
        }
    }
}
