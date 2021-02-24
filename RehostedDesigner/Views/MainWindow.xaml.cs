using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Activities;
using System.Activities.Debugger;
using System.Activities.Presentation;
using System.Activities.Presentation.Debug;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Activities.Presentation.Toolbox;
using System.Activities.Presentation.Validation;
using System.Activities.Presentation.View;
using System.Activities.Tracking;
using System.Activities.Validation;
using System.Reflection;
using System.IO;
using System.Activities.XamlIntegration;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using RehostedWorkflowDesigner.Helpers;
using System.Diagnostics;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Gat.Controls;
using Newtonsoft.Json.Linq;

namespace RehostedWorkflowDesigner.Views
{
	public sealed partial class MainWindow
	{
		private const string All = "*";

		private WorkflowDesigner _wfDesigner = new WorkflowDesigner();
		private readonly ToolboxControl _wfToolbox = new ToolboxControl();
		private readonly SimulatorTrackingParticipant _executionLog = new SimulatorTrackingParticipant
		{
			TrackingProfile = new TrackingProfile
			{
				Name = "SimulatorTrackingProfile",
				Queries =
				{
					new CustomTrackingQuery
					{
						Name = All,
						ActivityName = All
					},
					new WorkflowInstanceQuery
					{
						// Limit workflow instance tracking records for started and completed workflow states
						States = { WorkflowInstanceStates.Started, WorkflowInstanceStates.Completed }
					},
					new ActivityStateQuery
					{
						// Subscribe for track records from all activities for all states
						ActivityName = All,
						States = { All },

						// Extract workflow variables and arguments as a part of the activity tracking record
						// VariableName = "*" allows for extraction of all variables in the scope of the activity
						Variables = { All }
					}
				}
			}
		};
		private readonly ConsoleWriter _consoleWriter = new ConsoleWriter();

		private WorkflowApplication _wfApp;
		private string _currentWorkflowFile = string.Empty;

		private readonly Dictionary<object, SourceLocation> _designerSourceLocationMapping = new Dictionary<object, SourceLocation>();
		private Dictionary<object, SourceLocation> _wfElementToSourceLocationMap;

		private AutoResetEvent _resumeRuntimeFromHost;
		private readonly List<SourceLocation> _breakpointList = new List<SourceLocation>();

		public MainWindow()
		{
			InitializeComponent();

			DataContext = this;

			// load all available workflow activities from loaded assemblies 
			InitializeActivitiesToolbox();

			// initialize designer
			CreateNewWorkflowDesigner(() => CustomWfDesigner.NewInstance());

			_consoleWriter.WriteEvent += ConsoleWriter_WriteEvent;
			_consoleWriter.WriteLineEvent += ConsoleWriter_WriteEvent;

			Console.SetOut(_consoleWriter);

			_executionLog.TrackingRecordReceived += ExecutionLog_OnTrackingRecordReceived;
		}

		public ObservableCollection<TrackingRecordInfo> TrackingRecordInfos { get; set; } = new ObservableCollection<TrackingRecordInfo>();

		private void WorkflowDesigner_OnModelChanged(object sender, EventArgs e)
		{
			ResetUI();
			RegenerateSourceDebuggerMappings();
		}

		private void ConsoleWriter_WriteEvent(object sender, ConsoleWriterEventArgs e)
		{
			Dispatcher.Invoke(DispatcherPriority.SystemIdle, (Action)(() =>
			{
				ConsoleOutput.Text += e.Value + Environment.NewLine;
			}));
		}

		private void ExecutionLog_OnTrackingRecordReceived(object sender, TrackingEventArgs e)
		{
			if (e.Activity == null)
			{
				return;
			}

			Debug.WriteLine($"<+=+=+=+> Activity Tracking Record Received for ActivityId: {e.Activity.Id}, record: {e.Record} ");

			ShowDebug(_wfElementToSourceLocationMap[e.Activity]);

			Dispatcher.Invoke(DispatcherPriority.SystemIdle, (Action)(() =>
			{
				// updates ConsoleExecutionLog
				var tri = new TrackingRecordInfo(e.Record, e.Timeout, e.Activity, _wfElementToSourceLocationMap[e.Activity]);
				TrackingRecordInfos.Add(tri);

				// Add a sleep so that the debug adornments are visible to the user
				Thread.Sleep(TimeSpan.FromMilliseconds(500));
			}));
		}

		// Provide Debug Adornment on the selected Activity
		private void ConsoleExecutionLog_SelectionChanged(object sender, RoutedEventArgs e)
		{
			var tri = (TrackingRecordInfo)ConsoleExecutionLog.SelectedItem;

			Dispatcher.Invoke(DispatcherPriority.Normal, (Action)(() =>
			{
				try
				{
					// Tell Debug Service that the Line Clicked is _______
					_wfDesigner.DebugManagerView.CurrentLocation = tri?.SourceLocation;
				}
				catch (Exception)
				{
					// If the user clicks other than on the tracking records themselves.
					_wfDesigner.DebugManagerView.CurrentLocation = new SourceLocation(_currentWorkflowFile, 1, 1, 1, 10);
				}
			}));
		}

		private void WorkflowErrors_SelectionChanged(object sender, RoutedEventArgs e)
		{
			var vei = (ValidationErrorInfo)WorkflowErrors.SelectedItem;
			if (vei is null)
			{
				return;
			}

			var modelSvc = _wfDesigner.Context.Services.GetService<ModelService>();
			var allModels = modelSvc.Find(modelSvc.Root, typeof(Activity));
			var errorModel = allModels.Single(mi => (mi.GetCurrentValue() as Activity)?.Id == vei.Id);
			Selection.SelectOnly(_wfDesigner.Context, errorModel);
			ModelItemExtensions.Focus(errorModel);
		}

		private void InitializeActivitiesToolbox()
		{
			try
			{
				var exeLoc = Assembly.GetExecutingAssembly().Location;
				var exeDir = Path.GetDirectoryName(exeLoc);
				var assyFilePath = Path.Combine(exeDir, "Activities.json");
				var jobj = JObject.Parse(File.ReadAllText(assyFilePath));
				var jarr = jobj["Activities"].Value<JArray>();
				var assyNames = jarr.ToObject<List<string>>();

				foreach (var assyName in assyNames)
				{
					AppDomain.CurrentDomain.Load(assyName);
				}

				// get all loaded assemblies
				IEnumerable<Assembly> appAssemblies = AppDomain
					.CurrentDomain
					.GetAssemblies()
					.OrderBy(a => a.GetName().Name);

				// check if assemblies contain activities
				foreach (var activityLibrary in appAssemblies)
				{
					var wfToolboxCategory = new ToolboxCategory(activityLibrary.GetName().Name);
					var activities = from
										activityType in activityLibrary.GetExportedTypes()
									 where
										 (activityType.IsSubclassOf(typeof(Activity))
										 || activityType.IsSubclassOf(typeof(NativeActivity))
										 || activityType.IsSubclassOf(typeof(DynamicActivity))
										 || activityType.IsSubclassOf(typeof(ActivityWithResult))
										 || activityType.IsSubclassOf(typeof(AsyncCodeActivity))
										 || activityType.IsSubclassOf(typeof(CodeActivity))
										 || activityType == typeof(System.Activities.Core.Presentation.Factories.ForEachWithBodyFactory<Type>)
										 || activityType == typeof(System.Activities.Statements.FlowNode)
										 || activityType == typeof(System.Activities.Statements.State)
										 || activityType == typeof(System.Activities.Core.Presentation.FinalState)
										 || activityType == typeof(System.Activities.Statements.FlowDecision)
										 || activityType == typeof(System.Activities.Statements.FlowNode)
										 || activityType == typeof(System.Activities.Statements.FlowStep)
										 || activityType == typeof(System.Activities.Statements.FlowSwitch<Type>)
										 || activityType == typeof(System.Activities.Statements.ForEach<Type>)
										 || activityType == typeof(System.Activities.Statements.Switch<Type>)
										 || activityType == typeof(System.Activities.Statements.TryCatch)
										 || activityType == typeof(System.Activities.Statements.While))
										 && activityType.IsVisible
										 && activityType.IsPublic
										 && !activityType.IsNested
										 && !activityType.IsAbstract
										 && activityType.GetConstructor(Type.EmptyTypes) != null
									 orderby
										 activityType.Name
									 select
										 new ToolboxItemWrapper(activityType);

					activities.ToList().ForEach(wfToolboxCategory.Add);

					if (wfToolboxCategory.Tools.Count > 0)
					{
						_wfToolbox.Categories.Add(wfToolboxCategory);
					}
				}

				// fixed ForEach
				_wfToolbox.Categories.Add(
					   new ToolboxCategory
					   {
						   CategoryName = "CustomForEach",
						   Tools =
						   {
								new ToolboxItemWrapper(typeof(System.Activities.Core.Presentation.Factories.ForEachWithBodyFactory<>)),
								new ToolboxItemWrapper(typeof(System.Activities.Core.Presentation.Factories.ParallelForEachWithBodyFactory<>))
						   }
					   }
				);

				var activitiesCount = _wfToolbox.Categories.Sum(toolboxCategory => toolboxCategory.Tools.Count);
				LabelStatusBar.Content = $"Loaded Activities: {activitiesCount}";
				WfToolboxBorder.Child = _wfToolbox;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
		}

		private void WfExecutionFinished()
		{
			_resumeRuntimeFromHost = null;

			// This is to remove the final debug adornment
			Dispatcher.Invoke(DispatcherPriority.Render, (Action)(() =>
			{
				_wfDesigner.DebugManagerView.CurrentLocation = new SourceLocation(_currentWorkflowFile, 1, 1, 1, 10);
			}));
		}

		// Show the Debug Adornment
		private void ShowDebug(SourceLocation srcLoc)
		{
			Dispatcher.Invoke(DispatcherPriority.Render, (Action)(() =>
			{
				_wfDesigner.DebugManagerView.CurrentLocation = srcLoc;
			}));

			// Check if this is where any BP is set
			var isBreakpointHit = false;
			foreach (var src in _breakpointList)
			{
				if (src.StartLine == srcLoc.StartLine &&
					src.EndLine == srcLoc.EndLine)
				{
					isBreakpointHit = true;
				}
			}

			if (isBreakpointHit)
			{
				_resumeRuntimeFromHost.WaitOne();
			}
		}

		private Dictionary<string, Activity> BuildActivityIdToWfElementMap(Dictionary<object, SourceLocation> wfElementToSourceLocationMap)
		{
			var map = new Dictionary<string, Activity>();

			foreach (var instance in wfElementToSourceLocationMap.Keys)
			{
				if (instance is Activity wfElement)
				{
					map.Add(wfElement.Id, wfElement);
				}
			}

			return map;
		}

		private Dictionary<object, SourceLocation> UpdateSourceLocationMappingInDebuggerService()
		{
			var sourceLocationMapping = new Dictionary<object, SourceLocation>();
			var rootInstance = GetRootInstance();
			var runtimeRootElement = GetRootRuntimeWorkflowElement();

			if (rootInstance != null &&
				runtimeRootElement != null)
			{
				var documentRootElement = GetRootWorkflowElement(rootInstance);
				SourceLocationProvider.CollectMapping(
					runtimeRootElement,
					documentRootElement,
					sourceLocationMapping,
					_wfDesigner.Context.Items.GetValue<WorkflowFileItem>().LoadedFile);

				// Collect the mapping between the Model Item tree and its underlying source location
				SourceLocationProvider.CollectMapping(
					documentRootElement,
					documentRootElement,
					_designerSourceLocationMapping,
					_wfDesigner.Context.Items.GetValue<WorkflowFileItem>().LoadedFile);
			}

			// Notify the DebuggerService of the new sourceLocationMapping.
			// When rootInstance == null, it'll just reset the mapping.
			// DebuggerService debuggerService = debuggerService as DebuggerService;
			var debuggerService = (DebuggerService)_wfDesigner.DebugManagerView;
			debuggerService.UpdateSourceLocations(_designerSourceLocationMapping);

			return sourceLocationMapping;
		}

		#region Helper Methods

		private object GetRootInstance()
		{
			var modelService = _wfDesigner.Context.Services.GetService<ModelService>();
			return modelService?.Root.GetCurrentValue();
		}

		// Get root WorkflowElement.  Currently only handle when the object is ActivitySchemaType or WorkflowElement.
		// May return null if it does not know how to get the root activity.
		private Activity GetRootWorkflowElement(object rootModelObject)
		{
			Debug.Assert(rootModelObject != null, "Cannot pass null as rootModelObject");

			Activity rootWorkflowElement;
			if (rootModelObject is IDebuggableWorkflowTree debuggableWorkflowTree)
			{
				rootWorkflowElement = debuggableWorkflowTree.GetWorkflowRoot();
			}
			else // Loose xaml case.
			{
				rootWorkflowElement = rootModelObject as Activity;
			}

			return rootWorkflowElement;
		}

		private Activity GetRootRuntimeWorkflowElement()
		{
			// get workflow source from designer
			_wfDesigner.Flush();
			var workflowStream = new MemoryStream(Encoding.Default.GetBytes(_wfDesigner.Text));

			var settings = new ActivityXamlServicesSettings()
			{
				CompileExpressions = true
			};

			var enclosingAct = ActivityXamlServices.Load(workflowStream, settings);
			if (ActivityValidationServices.Validate(enclosingAct).Errors.Any())
			{
				return null;
			}
			WorkflowInspectionServices.CacheMetadata(enclosingAct);

			var enumerator1 = WorkflowInspectionServices.GetActivities(enclosingAct).GetEnumerator();
			// Get the first child of the x:class
			enumerator1.MoveNext();
			var root = enumerator1.Current;

			return root;
		}

		private void ResetUI()
		{
			TrackingRecordInfos.Clear();
			ConsoleOutput.Clear();
		}

		private void RegenerateSourceDebuggerMappings()
		{
			// Updating the mapping between Model item and Source Location before we run the workflow so that BP setting can re-use that information from the DesignerSourceLocationMapping.
			_designerSourceLocationMapping.Clear();
			_wfElementToSourceLocationMap = UpdateSourceLocationMappingInDebuggerService();
			_executionLog.ActivityIdToWorkflowElementMap = BuildActivityIdToWfElementMap(_wfElementToSourceLocationMap);
		}

		#endregion

		#region Commands Handlers - Executed - New, Open, Save, Run

		private void CmdExit(object sender, ExecutedRoutedEventArgs e)
		{
			Application.Current.Shutdown();
		}

		private void CmdWorkflowUndo(object sender, ExecutedRoutedEventArgs e)
		{
			_wfDesigner.Context.Services.GetService<UndoEngine>().Undo();
		}

		private void CmdWorkflowRedo(object sender, ExecutedRoutedEventArgs e)
		{
			_wfDesigner.Context.Services.GetService<UndoEngine>().Redo();
		}

		private void CmdAbout(object sender, ExecutedRoutedEventArgs e)
		{
			var appLogo = new BitmapImage(new Uri(@"pack://application:,,,/Rehosted%20WF%20Designer;component/Resources/ApplicationLogo.bmp", UriKind.Absolute));
			var pubLogo = new BitmapImage(new Uri(@"pack://application:,,,/Rehosted%20WF%20Designer;component/Resources/GitHub-Mark.png", UriKind.Absolute));
			var about = new About
			{
				IsSemanticVersioning = true,
				ApplicationLogo = appLogo,
				PublisherLogo = pubLogo,
				HyperlinkText = "https://github.com/TrevorDArcyEvans/Rehosted-Workflow-Designer",
				AdditionalNotes = string.Empty
			};

			about.Show();
		}

		private void CmdWorkflowRun(object sender, ExecutedRoutedEventArgs e)
		{
			if (_resumeRuntimeFromHost is null)
			{
				ResetUI();
				RegenerateSourceDebuggerMappings();

				_resumeRuntimeFromHost = new AutoResetEvent(false);

				// get workflow source from designer
				_wfDesigner.Flush();
				var workflowStream = new MemoryStream(Encoding.Default.GetBytes(_wfDesigner.Text));

				var settings = new ActivityXamlServicesSettings
				{
					CompileExpressions = true
				};

				var activityExecute = ActivityXamlServices.Load(workflowStream, settings) as DynamicActivity;
				if (ActivityValidationServices.Validate(activityExecute).Errors.Any())
				{
					WfExecutionFinished();
					return;
				}

				// configure workflow application
				_wfApp = new WorkflowApplication(activityExecute);
				_wfApp.Extensions.Add(_executionLog);
				_wfApp.Completed = ev => WfExecutionFinished();
				_wfApp.Aborted = ev => WfExecutionFinished();

				// execute 
				ThreadPool.QueueUserWorkItem(context =>
				{
					// Start the Runtime
					_wfApp.Run(TimeSpan.FromHours(1));
				});
			}
			else
			{
				_resumeRuntimeFromHost.Set();
			}
		}

		private void CmdWorkflowStop(object sender, ExecutedRoutedEventArgs e)
		{
			// manual stop
			_wfApp?.Abort("Stopped by User");
		}

		private void CmdWorkflowSave(object sender, ExecutedRoutedEventArgs e)
		{
			if (_currentWorkflowFile == string.Empty)
			{
				var dialogSave = new SaveFileDialog
				{
					Title = "Save Workflow",
					Filter = "Workflows (.xaml)|*.xaml"
				};
				if (dialogSave.ShowDialog() == true)
				{
					_wfDesigner.Save(dialogSave.FileName);
					_currentWorkflowFile = dialogSave.FileName;
				}
			}
			else
			{
				_wfDesigner.Save(_currentWorkflowFile);
			}
		}

		private void CmdWorkflowNew(object sender, ExecutedRoutedEventArgs e)
		{
			CreateNewWorkflowDesigner(() => CustomWfDesigner.NewInstance());
		}

		private void CmdWorkflowNewVB(object sender, ExecutedRoutedEventArgs e)
		{
			CreateNewWorkflowDesigner(() => CustomWfDesigner.NewInstanceVB());
		}

		private void CmdWorkflowNewCSharp(object sender, ExecutedRoutedEventArgs e)
		{
			CreateNewWorkflowDesigner(() => CustomWfDesigner.NewInstanceCSharp());
		}

		private void CmdWorkflowOpen(object sender, ExecutedRoutedEventArgs e)
		{
			var dialogOpen = new OpenFileDialog
			{
				Title = "Open Workflow",
				Filter = "Workflows (.xaml)|*.xaml"
			};

			if (dialogOpen.ShowDialog() == true)
			{
				using (new StreamReader(dialogOpen.FileName, true))
				{
					CreateNewWorkflowDesigner(() => CustomWfDesigner.NewInstance(dialogOpen.FileName), dialogOpen.FileName);
				}
			}
		}

		private void CreateNewWorkflowDesigner(Func<WorkflowDesigner> createWfDesigner, string currentWorkflowFile = "")
		{
			// unhook previous event handler
			_wfDesigner.ModelChanged -= WorkflowDesigner_OnModelChanged;

			_currentWorkflowFile = currentWorkflowFile;
			_wfDesigner = createWfDesigner();
			WfDesignerBorder.Child = _wfDesigner.View;
			WfPropertyBorder.Child = _wfDesigner.PropertyInspectorView;

			_wfDesigner.ModelChanged += WorkflowDesigner_OnModelChanged;

			var validationErrorService = new ValidationErrorService(WorkflowErrors.Items);
			_wfDesigner.Context.Services.Publish<IValidationErrorService>(validationErrorService);

			ResetUI();
			RegenerateSourceDebuggerMappings();
		}

		private void CmdToggleBreakpoint(object sender, ExecutedRoutedEventArgs e)
		{
			var mi = _wfDesigner.Context.Items.GetValue<Selection>().PrimarySelection;
			if (!(mi?.GetCurrentValue() is Activity activity))
			{
				return;
			}

			if (!_designerSourceLocationMapping.ContainsKey(activity))
			{
				return;
			}

			var srcLoc = _designerSourceLocationMapping[activity];
			if (!_breakpointList.Contains(srcLoc))
			{
				_wfDesigner.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint(srcLoc, BreakpointTypes.Bounded | BreakpointTypes.Enabled);
				_breakpointList.Add(srcLoc);
			}
			else
			{
				_wfDesigner.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint(srcLoc, BreakpointTypes.None);
				_breakpointList.Remove(srcLoc);
			}
		}

		#endregion
	}
}
