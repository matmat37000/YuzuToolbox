using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Godot.Collections;
using Gtk;
using Mono.Unix;
using ProgressBar = Godot.ProgressBar;
using Window = Godot.Window;
using WindowsShortcutFactory;
using Environment = System.Environment;


public partial class Home : Control
{
	[Export()] private float _appVersion = 2.1f;
	[Export()] private float _settingsVersion = 1.7f;

	[Export()] private Godot.Image _icon;
	[Export()] private AudioStreamPlayer _backgroundAudio;
	[Export()] private ColorRect _header;
	[Export()] private Godot.Label _headerLabel;
	[Export()] private TextureRect _darkBg;
	[Export()] private TextureRect _lightBg;
	[Export()] private Godot.VSeparator _headerSeparator;
	[Export()] private OptionButton _versionButton;
	[Export()] private CheckBox _createShortcutButton;
	[Export()] private Godot.Button _locationButton;
	[Export()] private Godot.Button _downloadButton;
	[Export()] private CheckBox _clearShadersButton;
	[Export()] private Godot.Button _shadersLocationButton;
	[Export()] private Panel _downloadWindow;
	[Export()] private ColorRect _downloadWindowApp;
	[Export()] private Godot.Label _downloadLabel;
	[Export()] private CheckBox _autoUnpackButton;
	[Export()] private ProgressBar _downloadProgressBar;
	[Export()] private CheckBox _customVersionCheckBox;
	[Export()] private SpinBox _customVersionSpinBox;
	[Export()] private Timer _downloadUpdateTimer;
	[Export()] private CheckBox _enableLightTheme;
	[Export()] private Popup _errorPopup;
	[Export()] private Godot.Label _errorLabel;
	[Export()] private Godot.Label _latestVersionLabel;
	[Export()] private HttpRequest _latestReleaseRequester;
	[Export()] private HttpRequest _downloadRequester;
	[Export()] private String _pineappleLatestUrl;
	[Export()] private String _pineappleDownloadBaseUrl;
	[Export()] private String _windowsFolderName = "yuzu-windows-msvc-early-access";
	[Export()] private string _yuzuBaseString = "Yuzu-EA-";
	[Export()] private string _saveName;
	[Export()] private int _previousVersionsToAdd = 10;
	[Export()] private Array<Theme> _themes;
	[Export()] private Array<StyleBoxLine> _themesSeparator;
	[Export()] private TextureRect _extractWarning;
	[Export()] private TextureRect _downloadWarning;
	[Export()] private TextureRect _clearShadersWarning;

	private FileChooserDialog _fileChooser;
	private ResourceSaveManager _saveManager;
	private SettingsResource _settings;
	private String _osUsed;
	private string _yuzuExtensionString;
	private Theme _currentTheme;

	public override void _Ready()
	{
		// Sets minimum window size to prevent text clipping and UI breaking at smaller scales.
		DisplayServer.WindowSetMinSize(new Vector2I(1024, 576));
		_osUsed = OS.GetName();
		if (_osUsed == "Linux")
		{
			_saveName += ".AppImage";
			_yuzuExtensionString = ".AppImage";
			_autoUnpackButton.Disabled = true;
		}
		else if (_osUsed == "Windows")
		{
			_saveName += ".zip";
			_yuzuExtensionString = ".zip";
			_createShortcutButton.Disabled = true;
		}

		_saveManager = new ResourceSaveManager();
		_saveManager.Version = _settingsVersion;
		GetSettings();

		if (_settings.ShadersLocation == "")
		{
			_settings.ShadersLocation =
				$@"{System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData)}/yuzu/shader";
			_saveManager._settings = _settings;
			_saveManager.WriteSave();
		}

		_shadersLocationButton.Text = _settings.ShadersLocation;

		_locationButton.Text = _settings.SaveDirectory;

		// Call a request to get the latest versions and connect it to our GetNewVersions function
		_latestReleaseRequester.RequestCompleted += AddVersions;
		_latestReleaseRequester.Request(_pineappleLatestUrl);

		_downloadButton.Disabled = true;
		_downloadButton.Pressed += InstallSelectedVersion;
		_locationButton.Pressed += OnLocationButtonPressed;
		_downloadRequester.RequestCompleted += VersionDownloadCompleted;
		_downloadUpdateTimer.Timeout += UpdateDownloadBar;
		_downloadWindow.Visible = false;

		_shadersLocationButton.Pressed += OnShadersLocationButtonPressed;
		
		Resized += WindowResized;

		_customVersionCheckBox.Toggled += CustomVersionSpinBoxEditable;
		_customVersionSpinBox.Editable = false;

		_enableLightTheme.Toggled += SetTheme;

		_downloadButton.GrabFocus();

		_autoUnpackButton.Toggled += AutoUnpackToggled;
		_extractWarning.Visible = false;
		_downloadWarning.Visible = false;
		_clearShadersWarning.Visible = false;
	}

	private void SetTheme(bool enableLight)
	{
		_lightBg.Visible = enableLight;
		_darkBg.Visible = !enableLight;
		_currentTheme = enableLight ? _themes[1] : _themes[0];
		_header.Color = enableLight ? new Godot.Color(0.74117648601532f, 0.76470589637756f, 0.78039216995239f) : new Godot.Color(0.16862745583057f, 0.1803921610117f, 0.18823529779911f);
		_downloadWindowApp.Color = enableLight ? new Godot.Color(0.74117648601532f, 0.76470589637756f, 0.78039216995239f) : new Godot.Color(0.16862745583057f, 0.1803921610117f, 0.18823529779911f);
		_enableLightTheme.ButtonPressed = enableLight;
		_settings.LightModeEnabled = enableLight;
		_saveManager._settings = _settings;
		_saveManager.WriteSave();
		Theme = _currentTheme;
	}


	private void WindowResized()
	{
		float scaleRatio = (float)GetWindow().Size.X / 1920;
		_headerLabel.AddThemeFontSizeOverride("font_size", (int)(scaleRatio * 76));
		_latestVersionLabel.AddThemeFontSizeOverride("font_size", (int)(scaleRatio * 32));
		_currentTheme.DefaultFontSize = Mathf.Clamp((int)(scaleRatio * 35), 20, 50);
	}


	private void CustomVersionSpinBoxEditable(bool editable)
	{
		_customVersionSpinBox.Editable = editable;
		_versionButton.Disabled = editable;
	}
	
	
	private void InstallSelectedVersion()
	{
		int version;
		DeleteOldVersion();
		
		// Set old install (if it exists) to not be disabled anymore.
		if (_settings.InstalledVersion != -1)
		{
			_versionButton.SetItemDisabled(_versionButton.GetItemIndex(_settings.InstalledVersion), false);
		}

		if (_customVersionCheckBox.ButtonPressed)
		{
			version = (int)_customVersionSpinBox.Value;
		}
		else
		{
			int versionIndex = _versionButton.Selected;
			version = _versionButton.GetItemText(versionIndex).ToInt();
		}

		_customVersionCheckBox.Disabled = true;
		_versionButton.Disabled = true;
		_downloadButton.Disabled = true;
		_locationButton.Disabled = true;
		_settings.InstalledVersion = version;
		_downloadLabel.Text = "Downloading...";
		_downloadWindow.Visible = true;
		_downloadLabel.GrabFocus();
		_downloadRequester.DownloadFile = $@"{_settings.SaveDirectory}/{_saveName}";
		_downloadRequester.Request($@"{_pineappleDownloadBaseUrl}{version}/{_osUsed}-{_yuzuBaseString}{version}{_yuzuExtensionString}");
		_downloadUpdateTimer.Start();
	}


	private void VersionDownloadCompleted(long result, long responseCode, string[] headers, byte[] body)
	{
		_downloadUpdateTimer.Stop();
		_customVersionCheckBox.Disabled = false;
		_downloadButton.Disabled = false;
		_locationButton.Disabled = false;
		_versionButton.Disabled = false;
		if (result == (int)HttpRequest.Result.Success)
		{
			_saveManager._settings = _settings;
			_saveManager.WriteSave();
			_downloadProgressBar.Value = 100;
			_downloadLabel.Text = "Successfully Downloaded!";
			
			AddInstalledVersion();
			UnpackAndSetPermissions();
			if (_createShortcutButton.ButtonPressed)
			{
				_downloadWindow.Visible = false;
				CreateShortcut();
			}
			_downloadWindow.Visible = false;
		}
		else
		{
			_downloadProgressBar.Value = 0;
		}
	}
	
	
	private void UpdateDownloadBar()
	{
		_downloadProgressBar.Value = (float)_downloadRequester.GetDownloadedBytes()/_downloadRequester.GetBodySize() * 100;
	}


	private void CreateShortcut()
	{
		String linuxShortcutName = "yuzu-ea.desktop";
		String windowsShortcutName = "yuzu-ea.lnk";
		String iconPath = $@"{_settings.SaveDirectory}/Icon.png";
		
		if (_osUsed == "Linux")
		{
			_icon.SavePng(iconPath);
			string shortcutContent = $@"
[Desktop Entry]
Comment=Nintendo Switch video game console emulator
Exec={GetExistingVersion()}
GenericName=Switch Emulator
Icon={iconPath}
MimeType=
Name=Yuzu-EA
Path=
StartupNotify=true
Terminal=false
TerminalOptions=
Type=Application
Keywords=Nintendo;Switch;
Categories=Game;Emulator;Qt;
";

			if (Directory.Exists("/usr/share/applications/"))
			{
				string shortcutPath = $@"/usr/share/applications/{linuxShortcutName}";

				try
				{
					string tempShortcutPath = $@"{_settings.SaveDirectory}/{linuxShortcutName}";
					File.WriteAllText(tempShortcutPath, shortcutContent);
					ProcessStartInfo startInfo = new ProcessStartInfo
					{
						FileName = "pkexec",
						Arguments = $"mv {tempShortcutPath} {shortcutPath}",
						UseShellExecute = false
					};

					Process process = new Process { StartInfo = startInfo };
					process.Start();
					process.WaitForExit();
				}
				catch (Exception shortcutError)
				{
					shortcutPath = $@"{_settings.SaveDirectory}/{linuxShortcutName}";
					ErrorPopup($@"Error creating shortcut, creating new at {shortcutPath}. Error:{shortcutError}");
					File.WriteAllText(shortcutPath, shortcutContent);
				}
			}
		}
		else if (_osUsed == "Windows")
		{
			string commonStartMenuPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonStartMenu);
			string yuzuStartMenuPath = Path.Combine(commonStartMenuPath, "Programs", "yuzu-ea");
			string yuzuShortcutPath = Path.Combine(yuzuStartMenuPath, windowsShortcutName);
			var windowsShortcut = new WindowsShortcut
			{
				Path = GetExistingVersion()
			};


			try
			{
				if (!Directory.Exists(yuzuStartMenuPath))
				{
					Directory.CreateDirectory(yuzuStartMenuPath);
				}
				
				windowsShortcut.Save(yuzuShortcutPath);
			}
			catch (Exception shortcutError)
			{
				yuzuShortcutPath = $@"{_settings.SaveDirectory}/{windowsShortcutName}";
				ErrorPopup($@"cannot create shortcut, ensure app is running as admin. Placing instead at {yuzuShortcutPath}. Exception:{shortcutError}");
				windowsShortcut.Save(yuzuShortcutPath);
			}
			
		}
	}
	

	private void AddVersions(long result, long responseCode, string[] headers, byte[] body)
	{
		if (result == (int)HttpRequest.Result.Success)
		{
			int latestVersion = GetLatestVersion(Encoding.UTF8.GetString(body));
			_customVersionSpinBox.Value = latestVersion;
			_latestVersionLabel.Text = $"Latest: {latestVersion.ToString()}";

			//Add a version item for the latest and the dictated amount of previous versions.
			for (int previousIndex = 0; previousIndex < _previousVersionsToAdd; previousIndex++)
			{
				_versionButton.AddItem((latestVersion-previousIndex).ToString(), latestVersion-previousIndex);
			}

			//Checks if there is already a version installed, and if so adds it.
			if (_settings.InstalledVersion != -1)
			{
				AddInstalledVersion();
			}

			_downloadButton.Disabled = false;
		}
		else
		{
			CallDeferred("ErrorPopup", "Failed to get latest versions error code: " + responseCode);
		}
	}


	private void AddInstalledVersion()
	{
		var installedVersion = _settings.InstalledVersion;
		var selectedIndex = _versionButton.GetItemIndex(installedVersion);
		_customVersionSpinBox.Value = installedVersion;

		// Checks if the item was already added, if so sets it as current, otherwise adds a new item entry for it.
		if (selectedIndex >= 0)
		{
			_versionButton.Selected = selectedIndex;
		}
		else
		{
			_versionButton.AddItem(installedVersion.ToString(), installedVersion);
			selectedIndex = _versionButton.GetItemIndex(installedVersion);
			_versionButton.Selected = selectedIndex;
		}
		_versionButton.SetItemDisabled(selectedIndex, true);
	}


	private int GetLatestVersion(String rawVersionData)
	{
		string searchName = $@"{_osUsed}-{_yuzuBaseString}";
		int versionIndex = rawVersionData.Find(searchName);

		// Using our starting index subtract the index of our extension from it and add 1 to get the length of the version
		int versionLength =  rawVersionData.Find(_yuzuExtensionString) -versionIndex -searchName.Length;

		// Return version by starting at our start index (accounting for our search string) and going the previously determined length
		return rawVersionData.Substring(versionIndex + searchName.Length, versionLength).ToInt();
	}


	private void UnpackAndSetPermissions()
	{
		string yuzuPath = $@"{_settings.SaveDirectory}/{_saveName}";
		if (_osUsed == "Linux")
		{
			var yuzuFile = new Mono.Unix.UnixFileInfo(yuzuPath)
			{
				FileAccessPermissions = FileAccessPermissions.UserReadWriteExecute
			};
		}
		else if (_osUsed == "Windows")
		{
			if (_autoUnpackButton.ButtonPressed)
			{
				System.IO.Compression.ZipFile.ExtractToDirectory(yuzuPath, _settings.SaveDirectory);
				String yuzuWindowsDirectory = $@"{_settings.SaveDirectory}/{_windowsFolderName}";
				if (Directory.Exists(yuzuWindowsDirectory))
				{
					MoveFilesAndDirs(yuzuWindowsDirectory, _settings.SaveDirectory);
				}
			}
		}
	}

	
	private void GetSettings()
	{
		if (ResourceSaveManager.SaveExists())
		{
			var lastSave = (ResourceSaveManager)ResourceSaveManager.LoadSaveGame();
			
			if (lastSave.Version != _settingsVersion)
			{
				CallDeferred("ErrorPopup", $@"Error loading settings, version mismatch detected. Settings have been regenerated.");
				_saveManager._settings = new SettingsResource();
				_saveManager.WriteSave();
			}
			_settings = lastSave._settings;
		}
		else
		{
			_settings = new SettingsResource();
			_saveManager._settings = _settings;
			_saveManager.WriteSave();
		}
		SetTheme(_settings.LightModeEnabled);

	}

	private String GetExistingVersion()
	{
		if (DirAccess.DirExistsAbsolute(_settings.SaveDirectory))
		{
			var previousSave = DirAccess.Open(_settings.SaveDirectory);

			foreach (var file in previousSave.GetFiles())
			{
				if (file.GetExtension() == "AppImage" || file.GetBaseName() == "yuzu")
				{
					return $@"{_settings.SaveDirectory}/{file}";
				}
			}
		}	

		return "";
	}


	private void DeleteOldVersion()
	{
		var oldVersion = GetExistingVersion();
		
		if (_osUsed == "Linux")
		{
			if (oldVersion != "")
			{
				File.Delete(oldVersion);
			}
		}
		
		else if (_osUsed == "Windows")
		{
			if (_autoUnpackButton.ButtonPressed)
			{
				DeleteDirectoryContents(_settings.SaveDirectory);
			}
			else
			{
				if (oldVersion != "")
				{
					File.Delete(oldVersion);
				}
			}
		}

		if (_clearShadersButton.ButtonPressed)
		{
			if (Directory.Exists(_settings.ShadersLocation))
			{
				DeleteDirectoryContents(_settings.ShadersLocation);
			}
			else
			{
				ErrorPopup("failed to find shaders location");
			}

		}

	}


	private void OnShadersLocationButtonPressed()
	{
		OpenFileChooser(ref _settings.ShadersLocation);
		_shadersLocationButton.Text = _settings.ShadersLocation;
		SaveSettings();
	}
	
	
	private void OnLocationButtonPressed()
	{
		OpenFileChooser(ref _settings.SaveDirectory);
		_locationButton.Text = _settings.SaveDirectory;
		SaveSettings();
	}
	
	
	private void OpenFileChooser(ref string returnObject)
	{
		try
		{
			Application.Init();
		}
		catch (Exception gtkError)
		{
			ErrorPopup("opening GTK window failed: " + gtkError);
		}
		_fileChooser = new FileChooserDialog("Select a File", null, FileChooserAction.SelectFolder);

		// Add a "Cancel" button to the dialog
		_fileChooser.AddButton("Cancel", ResponseType.Cancel);

		// Add an "Open" button to the dialog
		_fileChooser.AddButton("Open", ResponseType.Ok);

		// Set the initial directory
		_fileChooser.SetCurrentFolder("/");

		// Connect the response signal, I would like to directly pass in the return object, but this isn't possible 
		// in a lambda, so we create a temp value to hold it and then assign it to that value after.
		string tempReturnString = returnObject;
		_fileChooser.Response += (sender, args) => OnFileChooserResponse(sender, args, ref tempReturnString);

		// Show the dialog
		_fileChooser.Show();
		Application.Run();
		
		// Sets our original object back to be the returned temporary string.
		returnObject = tempReturnString;
	}

	private void OnFileChooserResponse(object sender, ResponseArgs args, ref string returnObject)
	{
		if (args.ResponseId == ResponseType.Ok)
		{
			// The user selected a file
			returnObject = _fileChooser.File.Path;
		}

		// Clean up resources
		_fileChooser.Dispose();
		Application.Quit();
	}


	static void MoveFilesAndDirs(string sourceDirectory, string targetDirectory)
	{
		// Create the target directory if it doesn't exist
		if (!Directory.Exists(targetDirectory))
		{
			Directory.CreateDirectory(targetDirectory);
		}

		// Get all files and directories from the source directory
		string[] files = Directory.GetFiles(sourceDirectory);
		string[] directories = Directory.GetDirectories(sourceDirectory);

		// Move files to the target directory
		foreach (string file in files)
		{
			string fileName = Path.GetFileName(file);
			string targetPath = Path.Combine(targetDirectory, fileName);
			File.Move(file, targetPath);
		}

		// Move directories to the target directory
		foreach (string directory in directories)
		{
			string directoryName = Path.GetFileName(directory);
			string targetPath = Path.Combine(targetDirectory, directoryName);
			Directory.Move(directory, targetPath);
		}

		// Remove the source directory if it is empty
		if (Directory.GetFiles(sourceDirectory).Length == 0 && Directory.GetDirectories(sourceDirectory).Length == 0)
		{
			Directory.Delete(sourceDirectory);
		}
	}
	
	
	
	static void DeleteDirectoryContents(string directoryPath)
	{
		// Delete all files within the directory
		string[] files = Directory.GetFiles(directoryPath);
		foreach (string file in files)
		{
			File.Delete(file);
		}

		// Delete all subdirectories within the directory
		string[] directories = Directory.GetDirectories(directoryPath);
		foreach (string directory in directories)
		{
			DeleteDirectoryContents(directory); // Recursively delete subdirectory contents
			Directory.Delete(directory);
		}
	}
	

	private void ErrorPopup(String error)
	{
		_errorLabel.Text = $@"Error:{error}";
		_errorPopup.Visible = true;
		_errorPopup.InitialPosition = Window.WindowInitialPosition.Absolute;
		_errorPopup.PopupCentered();
	}
	
	private void ToggledMusicButton(bool musicEnabled)
	{
		if(musicEnabled) {_backgroundAudio.VolumeDb = -(20.0f * (1.0f - 0.5f) + 80.0f * 0.5f);}
		else {_backgroundAudio.VolumeDb = -20;}
	}

	private void ClearInstallationFolder()
	{
		DeleteDirectoryContents(_settings.SaveDirectory);
	}

	private void AutoUnpackToggled(bool unpackEnabled)
	{
		// If unpack is toggled off, ensures the create shortcut button is also disabled and turns off.
		_createShortcutButton.ButtonPressed = unpackEnabled && _createShortcutButton.ButtonPressed;
		_createShortcutButton.Disabled = !unpackEnabled;
		_downloadWarning.Visible = _extractWarning.Visible || unpackEnabled;
		_extractWarning.Visible = unpackEnabled;
	}

	private void ClearShadersToggle(bool clearEnabled)
	{
		_clearShadersWarning.Visible = clearEnabled;
		_downloadWarning.Visible = _extractWarning.Visible || clearEnabled;
	}


	private void SaveSettings()
	{
		_saveManager._settings = _settings;
		_saveManager.WriteSave();
	}
}


