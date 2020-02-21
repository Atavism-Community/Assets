// Project:         Daggerfall Tools For Unity
// Copyright:       Copyright (C) 2009-2015 Gavin Clayton
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Web Site:        http://www.dfworkshop.net
// Contact:         Gavin Clayton (interkarma@dfworkshop.net)
// Project Page:    https://github.com/Interkarma/daggerfall-unity

#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop.Utility;
using SimpleFileBrowser;

namespace DaggerfallWorkshop
{
    /// <summary>
    /// DaggerfallUnity main class.
    /// </summary>
#if UNITY_EDITOR
    [ExecuteInEditMode]
#endif
    [RequireComponent(typeof(WorldTime))]
    [RequireComponent(typeof(MaterialReader))]
    [RequireComponent(typeof(MeshReader))]
    [RequireComponent(typeof(SoundReader))]
    public class DaggerfallUnity : MonoBehaviour
    {
        [NonSerialized]
        public const string Version = "1.2.37";

        #region Structs
#pragma warning disable 0649
        [Serializable]
        private struct FiletypeIcon
        {
            public string extension;
            public Sprite icon;
        }

        [Serializable]
        private struct QuickLink
        {
#if UNITY_EDITOR || (!UNITY_WSA && !UNITY_WSA_10_0)
            public Environment.SpecialFolder target;
#endif
            public string name;
            public Sprite icon;
        }
#pragma warning restore 0649
        #endregion

        #region Inner Classes
        public class Filter
        {
            public readonly string name;
            public readonly HashSet<string> extensions;
            public readonly string defaultExtension;

            internal Filter(string name)
            {
                this.name = name;
                extensions = null;
                defaultExtension = null;
            }

            public Filter(string name, string extension)
            {
                this.name = name;

                extension = extension.ToLowerInvariant();
                extensions = new HashSet<string>() { extension };
                defaultExtension = extension;
            }

            public Filter(string name, params string[] extensions)
            {
                this.name = name;

                for (int i = 0; i < extensions.Length; i++)
                    extensions[i] = extensions[i].ToLowerInvariant();

                this.extensions = new HashSet<string>(extensions);
                defaultExtension = extensions[0];
            }

            public override string ToString()
            {
                string result = "";

                if (name != null)
                    result += name;

                if (extensions != null)
                {
                    if (name != null)
                        result += " (";

                    int index = 0;
                    foreach (string extension in extensions)
                    {
                        if (index++ > 0)
                            result += ", " + extension;
                        else
                            result += extension;
                    }

                    if (name != null)
                        result += ")";
                }

                return result;
            }
        }
        #endregion

        #region Constants
        private const string ALL_FILES_FILTER_TEXT = "All Files (.*)";
        private const string FOLDERS_FILTER_TEXT = "Folders";
        private string DEFAULT_PATH;

#if !UNITY_EDITOR && UNITY_ANDROID
		private const string SAF_PICK_FOLDER_QUICK_LINK_TEXT = "Pick Folder";
		private const string SAF_PICK_FOLDER_QUICK_LINK_PATH = "SAF_PICK_FOLDER";
#endif
        #endregion

        #region Static Variables
        public static bool IsOpen { get; private set; }

        public static bool Success { get; private set; }
        public static string Result { get; private set; }

        private static bool m_askPermissions = true;
        public static bool AskPermissions
        {
            get { return m_askPermissions; }
            set { m_askPermissions = value; }
        }

        private static bool m_singleClickMode = false;
        public static bool SingleClickMode
        {
            get { return m_singleClickMode; }
            set { m_singleClickMode = value; }
        }

        /*
        private static FileBrowser m_instance = null;
        private static FileBrowser Instance
        {
            get
            {
                if (m_instance == null)
                {
                    m_instance = Instantiate(Resources.Load<GameObject>("SimpleFileBrowserCanvas")).GetComponent<FileBrowser>();
                    DontDestroyOnLoad(m_instance.gameObject);
                    m_instance.gameObject.SetActive(false);
                }

                return m_instance;
            }
        }
        */
        #endregion

        #region Variables
#pragma warning disable 0649
        [Header("References")]

        //[SerializeField]
        //private FileBrowserMovement window;
        private RectTransform windowTR;

        //[SerializeField]
        //private FileBrowserItem itemPrefab;

        //[SerializeField]
        //private FileBrowserQuickLink quickLinkPrefab;

        //[SerializeField]
        //private Text titleText;

        //[SerializeField]
        //private Button backButton;

        //[SerializeField]
        //private Button forwardButton;

        //[SerializeField]
        //private Button upButton;

        //[SerializeField]
        //private InputField pathInputField;

        //[SerializeField]
        //private InputField searchInputField;

        [SerializeField]
        private RectTransform quickLinksContainer;

        [SerializeField]
        private RectTransform filesContainer;

        //[SerializeField]
        //private ScrollRect filesScrollRect;

        //[SerializeField]
        //private RecycledListView listView;

        //[SerializeField]
        //private InputField filenameInputField;

        //[SerializeField]
        //private Image filenameImage;

        //[SerializeField]
        //private Dropdown filtersDropdown;

        [SerializeField]
        private RectTransform filtersDropdownContainer;

        //[SerializeField]
        //private Text filterItemTemplate;

        //[SerializeField]
        //private Toggle showHiddenFilesToggle;

        //[SerializeField]
        //private Text submitButtonText;

        [Header("Icons")]

        [SerializeField]
        private Sprite folderIcon;

        [SerializeField]
        private Sprite driveIcon;

        [SerializeField]
        private Sprite defaultIcon;

        [SerializeField]
        private FiletypeIcon[] filetypeIcons;

        private Dictionary<string, Sprite> filetypeToIcon;

        [Header("Other")]

        public Color normalFileColor = Color.white;
        public Color hoveredFileColor = new Color32(225, 225, 255, 255);
        public Color selectedFileColor = new Color32(0, 175, 255, 255);

        public Color wrongFilenameColor = new Color32(255, 100, 100, 255);

        public int minWidth = 380;
        public int minHeight = 300;

        [SerializeField]
        private string[] excludeExtensions;

#pragma warning disable 0414
        [SerializeField]
        private QuickLink[] quickLinks;
#pragma warning restore 0414

        private HashSet<string> excludedExtensionsSet;
        private HashSet<string> addedQuickLinksSet;

        [SerializeField]
        private bool generateQuickLinksForDrives = true;
#pragma warning restore 0649

        private RectTransform rectTransform;

        private FileAttributes ignoredFileAttributes = FileAttributes.System;

        private FileSystemEntry[] allFileEntries;
        private readonly List<FileSystemEntry> validFileEntries = new List<FileSystemEntry>();

        private readonly List<Filter> filters = new List<Filter>();
        private Filter allFilesFilter;

        private bool showAllFilesFilter = true;

        private int currentPathIndex = -1;
        private readonly List<string> pathsFollowed = new List<string>();

        private bool canvasDimensionsChanged;

        // Required in RefreshFiles() function
        private UnityEngine.EventSystems.PointerEventData nullPointerEventData;
        #endregion

        #region Properties
        private string m_currentPath = string.Empty;
        private string CurrentPath
        {
            get { return m_currentPath; }
            set
            {
#if !UNITY_EDITOR && UNITY_ANDROID
				if( !FileBrowserHelpers.ShouldUseSAF )
#endif
                if (value != null)
                    value = GetPathWithoutTrailingDirectorySeparator(value.Trim());

                Debug.Log(value);

                if (value == null)
                    return;

                if (m_currentPath != value)
                {
                    if (!FileBrowserHelpers.DirectoryExists(value))
                        return;

                    m_currentPath = value;
                    //pathInputField.text = m_currentPath;

                    if (currentPathIndex == -1 || pathsFollowed[currentPathIndex] != m_currentPath)
                    {
                        currentPathIndex++;
                        if (currentPathIndex < pathsFollowed.Count)
                        {
                            pathsFollowed[currentPathIndex] = value;
                            for (int i = pathsFollowed.Count - 1; i >= currentPathIndex + 1; i--)
                                pathsFollowed.RemoveAt(i);
                        }
                        else
                            pathsFollowed.Add(m_currentPath);
                    }

                    //backButton.interactable = currentPathIndex > 0;
                    //forwardButton.interactable = currentPathIndex < pathsFollowed.Count - 1;
#if !UNITY_EDITOR && UNITY_ANDROID
					//if( !FileBrowserHelpers.ShouldUseSAF )
#endif
                    //upButton.interactable = Directory.GetParent(m_currentPath) != null;

                    m_searchString = string.Empty;
                    //searchInputField.text = m_searchString;

                    //filesScrollRect.verticalNormalizedPosition = 1;

                    //filenameImage.color = Color.white;
                    //if (m_folderSelectMode)
                        //filenameInputField.text = string.Empty;
                }

                RefreshFiles(true);
            }
        }

        private string m_searchString = string.Empty;
        private string SearchString
        {
            get
            {
                return m_searchString;
            }
            set
            {
                if (m_searchString != value)
                {
                    m_searchString = value;
                    //searchInputField.text = m_searchString;

                    RefreshFiles(false);
                }
            }
        }

        private int m_selectedFilePosition = -1;
        public int SelectedFilePosition { get { return m_selectedFilePosition; } }

        private FileBrowserItem m_selectedFile;
        private FileBrowserItem SelectedFile
        {
            get
            {
                return m_selectedFile;
            }
            set
            {
                if (value == null)
                {
                    if (m_selectedFile != null)
                        m_selectedFile.Deselect();

                    m_selectedFilePosition = -1;
                    m_selectedFile = null;
                }
                else if (m_selectedFilePosition != value.Position)
                {
                    if (m_selectedFile != null)
                        m_selectedFile.Deselect();

                    m_selectedFile = value;
                    m_selectedFilePosition = value.Position;

                    //if (m_folderSelectMode || !m_selectedFile.IsDirectory)
                        //filenameInputField.text = m_selectedFile.Name;

                    m_selectedFile.Select();
                }
            }
        }

        private bool m_acceptNonExistingFilename = false;
        private bool AcceptNonExistingFilename
        {
            get { return m_acceptNonExistingFilename; }
            set { m_acceptNonExistingFilename = value; }
        }

        private bool m_folderSelectMode = false;
        private bool FolderSelectMode
        {
            get
            {
                return m_folderSelectMode;
            }
            set
            {
                if (m_folderSelectMode != value)
                {
                    m_folderSelectMode = value;

                    if (m_folderSelectMode)
                    {
                        /*
                        filtersDropdown.options[0].text = FOLDERS_FILTER_TEXT;
                        filtersDropdown.value = 0;
                        filtersDropdown.RefreshShownValue();
                        filtersDropdown.interactable = false;
                        */
                    }
                    else
                    {
                        /*
                        filtersDropdown.options[0].text = filters[0].ToString();
                        filtersDropdown.interactable = true;
                        */
                    }

                    //Text placeholder = filenameInputField.placeholder as Text;
                    //if (placeholder != null)
                    //    placeholder.text = m_folderSelectMode ? "" : "Filename";
                }
            }
        }

        /*
        private string Title
        {
            //get { return titleText.text; }
            //set { titleText.text = value; }
        }

        private string SubmitButtonText
        {
            //get { return submitButtonText.text; }
            //set { submitButtonText.text = value; }
        }
        */
        #endregion

        #region Delegates
        public delegate void OnSuccess(string path);
        public delegate void OnCancel();
#if !UNITY_EDITOR && UNITY_ANDROID
		public delegate void DirectoryPickCallback( string rawUri, string name );
#endif

        private OnSuccess onSuccess;
        private OnCancel onCancel;
        #endregion

        #region Messages
        private void Awake()
        {
            instance = this;
            //static DaggerfallUnity instance = null;
        //public static DaggerfallUnity Instance

            //rectTransform = (RectTransform)transform;
            //windowTR = (RectTransform)window.transform;

        //ItemHeight = ((RectTransform)itemPrefab.transform).sizeDelta.y;
        nullPointerEventData = new UnityEngine.EventSystems.PointerEventData(null);

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS || UNITY_WSA || UNITY_WSA_10_0)
			DEFAULT_PATH = Application.persistentDataPath;
#else
            DEFAULT_PATH = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
#endif

#if !UNITY_EDITOR && UNITY_ANDROID
			if( FileBrowserHelpers.ShouldUseSAF )
			{
				// These UI elements have no use in Storage Access Framework mode (Android 10+)
				upButton.gameObject.SetActive( false );
				pathInputField.gameObject.SetActive( false );
				showHiddenFilesToggle.gameObject.SetActive( false );
			}
#endif

            //InitializeFiletypeIcons();
            filetypeIcons = null;

            //SetExcludedExtensions(excludeExtensions);
            excludeExtensions = null;

            //backButton.interactable = false;
            //forwardButton.interactable = false;
            //upButton.interactable = false;

            //filenameInputField.onValidateInput += OnValidateFilenameInput;

            //InitializeQuickLinks();
            quickLinks = null;

            allFilesFilter = new Filter(ALL_FILES_FILTER_TEXT);
            filters.Add(allFilesFilter);

            //window.Initialize(this);
            //listView.SetAdapter(this);
        }

        private void OnRectTransformDimensionsChange()
        {
            canvasDimensionsChanged = true;
        }

        private void LateUpdate()
        {
            if (canvasDimensionsChanged)
            {
                canvasDimensionsChanged = false;
                //EnsureWindowIsWithinBounds();
            }
        }

        private void OnApplicationFocus(bool focus)
        {
            if (focus)
                RefreshFiles(true);
        }
        #endregion

        private string GetPathWithoutTrailingDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            // Credit: http://stackoverflow.com/questions/6019227/remove-the-last-character-if-its-directoryseparatorchar-with-c-sharp
            try
            {
                if (Path.GetDirectoryName(path) != null)
                {
                    char lastChar = path[path.Length - 1];
                    if (lastChar == Path.DirectorySeparatorChar || lastChar == Path.AltDirectorySeparatorChar)
                        path = path.Substring(0, path.Length - 1);
                }
            }
            catch
            {
                return null;
            }

            return path;
        }

        public void RefreshFiles(bool pathChanged)
        {
            if (pathChanged)
            {
                /*
                if (!string.IsNullOrEmpty(m_currentPath))
                    allFileEntries = FileBrowserHelpers.GetEntriesInDirectory(m_currentPath);
                else
                    allFileEntries = null;
                    */
            }

            SelectedFile = null;

            //if (!showHiddenFilesToggle.isOn)
                //ignoredFileAttributes |= FileAttributes.Hidden;
            //else
                ignoredFileAttributes &= ~FileAttributes.Hidden;

            string searchStringLowercase = m_searchString.ToLower();

            //validFileEntries.Clear();

            if (allFileEntries != null)
            {
                for (int i = 0; i < allFileEntries.Length; i++)
                {
                    try
                    {
                        FileSystemEntry item = allFileEntries[i];

                        if (!item.IsDirectory)
                        {
                            if (m_folderSelectMode)
                                continue;

                            if ((item.Attributes & ignoredFileAttributes) != 0)
                                continue;

                            string extension = item.Extension.ToLowerInvariant();
                            if (excludedExtensionsSet.Contains(extension))
                                continue;

                            //HashSet<string> extensions = filters[filtersDropdown.value].extensions;
                            //if (extensions != null && !extensions.Contains(extension))
                              //  continue;
                        }
                        else
                        {
                            if ((item.Attributes & ignoredFileAttributes) != 0)
                                continue;
                        }

                        if (m_searchString.Length == 0 || item.Name.ToLower().Contains(searchStringLowercase))
                            validFileEntries.Add(item);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }

            //listView.UpdateList();

            // Prevent the case where all the content stays offscreen after changing the search string
            //filesScrollRect.OnScroll(nullPointerEventData);
        }

        #region Fields

        bool isReady = false;
        ContentReader reader;

        WorldTime worldTime;
        MaterialReader materialReader;
        MeshReader meshReader;
        SoundReader soundReader;
        
        #endregion

        #region Public Fields

        public string Arena2Path;
        public int ModelImporter_ModelID = 456;
        public string BlockImporter_BlockName = "MAGEAA01.RMB";
        public string CityImporter_CityName = "Daggerfall/Daggerfall";
        public string DungeonImporter_DungeonName = "Daggerfall/Privateer's Hold";

        // Performance options
        public bool Option_CombineRMB = true;
        public bool Option_CombineRDB = true;
        //public bool Option_CombineLocations = true;
        public bool Option_BatchBillboards = true;

        // Import options
        public bool Option_SetStaticFlags = true;
        public bool Option_AddMeshColliders = true;
        public bool Option_AddNavmeshAgents = true;
        public bool Option_DefaultSounds = true;
        public bool Option_SimpleGroundPlane = true;
        public bool Option_CloseCityGates = false;

        // Light options
        public bool Option_ImportPointLights = true;
        public bool Option_AnimatedPointLights = true;
        public string Option_PointLightTag = "Untagged";
#if UNITY_EDITOR
        public MonoScript Option_CustomPointLightScript = null;
#endif

        // Enemy options
        public bool Option_ImportEnemies = true;
        public bool Option_EnemyCharacterController = false;
        public bool Option_EnemyRigidbody = false;
        public bool Option_EnemyCapsuleCollider = false;
        public bool Option_EnemyNavMeshAgent = false;
        public bool Option_EnemyExampleAI = true;
        public string Option_EnemyTag = "Untagged";
        public float Option_EnemyRadius = 0.4f;
        public float Option_EnemySlopeLimit = 80f;
        public float Option_EnemyStepOffset = 0.4f;
        public bool Option_EnemyUseGravity = false;
        public bool Option_EnemyIsKinematic = true;
#if UNITY_EDITOR
        public MonoScript Option_CustomEnemyScript = null;
#endif

        // Time and space options
        public bool Option_AutomateTextureSwaps = true;
        public bool Option_AutomateSky = true;
        public bool Option_AutomateCityWindows = true;
        public bool Option_AutomateCityLights = true;
        public bool Option_AutomateCityGates = false;

        // Resource export options
#if UNITY_EDITOR
        public string Option_MyResourcesFolder = "Daggerfall Unity/Resources";
        public string Option_TerrainAtlasesSubFolder = "TerrainAtlases";
#endif

        #endregion

        #region Class Properties

        public bool IsReady
        {
            get { return isReady; }
        }

        public MaterialReader MaterialReader
        {
            get { return (materialReader != null) ? materialReader : materialReader = GetComponent<MaterialReader>(); }
        }

        public MeshReader MeshReader
        {
            get { return (meshReader != null) ? meshReader : meshReader = GetComponent<MeshReader>(); }
        }

        public SoundReader SoundReader
        {
            get { return (soundReader != null) ? soundReader : soundReader = GetComponent<SoundReader>(); }
        }

        public WorldTime WorldTime
        {
            get { return (worldTime != null) ? worldTime : worldTime = GetComponent<WorldTime>(); }
        }

        public ContentReader ContentReader
        {
            get
            {
                if (reader == null)
                    SetupContentReaders();
                return reader;
            }
        }

        #endregion

        #region Singleton

        static DaggerfallUnity instance = null;
        public static DaggerfallUnity Instance
        {
            get
            {
                if (instance == null)
                {
                    if (!FindDaggerfallUnity(out instance))
                    {
                        GameObject go = new GameObject();
                        go.name = "DaggerfallUnity";
                        instance = go.AddComponent<DaggerfallUnity>();
                    }
                }
                return instance;
            }
        }

        public static bool HasInstance
        {
            get
            {
                return (instance != null);
            }
        }

        #endregion

        #region Unity

        void Start()
        {
            CurrentPath = GetInitialPath(null);
            Arena2Path = CurrentPath;// + "\\arena2\\";
            Setup();
            SetupSingleton();
            SetupContentReaders();
        }

        void Update()
        {
            // Instance must be set up
            if (!Setup())
                return;

#if UNITY_EDITOR
            // Content readers must be ready
            // This is checked every update in editor as
            // code changes can reset singleton fields
            SetupContentReaders();
#endif
        }

        #endregion

        #region Startup and Shutdown

        private string GetInitialPath(string initialPath)
        {
            if (string.IsNullOrEmpty(initialPath) || !Directory.Exists(initialPath))
            {
                if (CurrentPath.Length == 0)
                    initialPath = DEFAULT_PATH;
                else
                    initialPath = CurrentPath;
            }

            m_currentPath = string.Empty; // Needed to correctly reset the pathsFollowed

            return initialPath;
        }

        public void Show(string initialPath)
        {
            //if (AskPermissions)
                //RequestPermission();

            SelectedFile = null;

            m_searchString = string.Empty;
            //searchInputField.text = m_searchString;

            //filesScrollRect.verticalNormalizedPosition = 1;

            //filenameInputField.text = string.Empty;
            //filenameImage.color = Color.white;

            IsOpen = true;
            Success = false;
            Result = null;

            gameObject.SetActive(true);

            CurrentPath = GetInitialPath(initialPath);
        }


        public bool Setup()
        {
            // Full validation is only performed in editor mode
            // This is to allow standalone builds to start with
            // no Arena2 data, or partial Arena2 data in Resources
            if (!isReady)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    // Attempt to autoload path
                    //LoadDeveloperArena2Path();
                    //CurrentPath = GetInitialPath(null);

                    Arena2Path = CurrentPath;// + "\\arena2\\";
                    Debug.LogWarning(CurrentPath);
                    Debug.LogWarning(Arena2Path);

                    // Must have a path set
                    if (string.IsNullOrEmpty(Arena2Path))
                        return false;

                    // Validate current path
                    if (ValidateArena2Path(Arena2Path))
                    {
                        isReady = true;
                        LogMessage("Arena2 path validated.", true);
                        SetupSingleton();
                        SetupContentReaders();
                    }
                    else
                    {
                        isReady = false;
                        return false;
                    }
                }
                else
                {
                    SetupSingleton();
                    SetupContentReaders();
                }
#else
                SetupSingleton();
                SetupContentReaders();
#endif

                isReady = true;
            }

            return true;
        }

        public bool ValidateArena2Path(string path)
        {
            DFValidator.ValidationResults results;
            DFValidator.ValidateArena2Folder(path, out results);

            Debug.LogWarning(path);

            return results.AppearsValid;
        }

        private void SetupSingleton()
        {
            if (instance == null)
                instance = this;
            else if (instance != this)
            {
                if (Application.isPlaying)
                {
                    LogMessage("Multiple DaggerfallUnity instances detected!", true);
                    Destroy(gameObject);
                }
            }
        }

        private void SetupContentReaders()
        {
            if (isReady)
            {
                if (reader == null)
                    reader = new ContentReader(Arena2Path, this);
            }
        }

#if UNITY_EDITOR && !UNITY_WEBPLAYER
        private void LoadDeveloperArena2Path()
        {
            const string devArena2Path = "devArena2Path";

            // Do nothing if path already set
            if (!string.IsNullOrEmpty(Arena2Path))
                return;

            // Attempt to load persistent dev path from Resources
            TextAsset path = Resources.Load<TextAsset>(devArena2Path);
            if (path)
            {
                if (Directory.Exists(path.text))
                {
                    // If it looks valid set this is as our path
                    if (ValidateArena2Path(path.text))
                    {
                        Debug.LogWarning(Arena2Path);
                        Arena2Path = path.text;
                        EditorUtility.SetDirty(this);
                    }
                }
            }
        }
#endif

        #endregion

        #region Public Static Methods

        public static void LogMessage(string message, bool showInEditor = false)
        {
            if (showInEditor || Application.isPlaying) Debug.Log(string.Format("DFTFU {0}: {1}", Version, message));
        }

        public static bool FindDaggerfallUnity(out DaggerfallUnity dfUnityOut)
        {
            dfUnityOut = GameObject.FindObjectOfType(typeof(DaggerfallUnity)) as DaggerfallUnity;
            if (dfUnityOut == null)
            {
                LogMessage("Could not locate DaggerfallUnity GameObject instance in scene!", true);
                return false;
            }

            return true;
        }

        #endregion

//        #region Editor Asset Export
//#if UNITY_EDITOR && !UNITY_WEBPLAYER
//        public void ExportTerrainTextureAtlases()
//        {
//            if (MaterialReader.IsReady)
//            {
//                TerrainAtlasBuilder.ExportTerrainAtlasTextureResources(materialReader.TextureReader, Option_MyResourcesFolder, Option_TerrainAtlasesSubFolder);
//            }
//        }
//#endif
//        #endregion
    }
}
