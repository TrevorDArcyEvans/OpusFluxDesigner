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
using System.Activities.Presentation.Services;
using System.Activities.Presentation.Toolbox;
using System.Activities.Presentation.View;
using System.Activities.Tracking;
using System.Reflection;
using System.IO;
using System.Activities.XamlIntegration;
using Microsoft.Win32;
using RehostedWorkflowDesigner.Helpers;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RehostedWorkflowDesigner.Views
{
	public partial class MainWindow : INotifyPropertyChanged
	{
		private const string All = "*";

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

		private readonly Dictionary<int, SourceLocation> _textLineToSourceLocationMap = new Dictionary<int, SourceLocation>();
		private readonly Dictionary<object, SourceLocation> _designerSourceLocationMapping = new Dictionary<object, SourceLocation>();
		private Dictionary<object, SourceLocation> _wfElementToSourceLocationMap;
		private int _lineIndex;

		private readonly List<SourceLocation> _breakpointList = new List<SourceLocation>();

		public MainWindow()
		{
			InitializeComponent();

			// load all available workflow activities from loaded assemblies 
			InitializeActivitiesToolbox();

			// initialize designer
			CreateNewWorkflowDesigner(() => CustomWfDesigner.NewInstance());

			_consoleWriter.WriteEvent += ConsoleWriter_WriteEvent;
			_consoleWriter.WriteLineEvent += ConsoleWriter_WriteEvent;

			Console.SetOut(_consoleWriter);

			_executionLog.TrackingRecordReceived += ExecutionLog_OnTrackingRecordReceived;
		}

		private void WorkflowDesigner_OnModelChanged(object sender, EventArgs e)
		{
			ResetUI();
			RegenerateSourceDebuggerMappings();
		}

		public void ConsoleWriter_WriteEvent(object sender, ConsoleWriterEventArgs e)
		{
			Dispatcher.Invoke(DispatcherPriority.SystemIdle, (Action)(() =>
			{
				ConsoleOutput.Text += e.Value + Environment.NewLine;
			}));
		}

		private void ExecutionLog_OnTrackingRecordReceived(object sender, TrackingEventArgs e)
		{
			if (e.Activity != null)
			{
				Debug.WriteLine($"<+=+=+=+> Activity Tracking Record Received for ActivityId: {e.Activity.Id}, record: {e.Record} ");

				ShowDebug(_wfElementToSourceLocationMap[e.Activity]);

				Dispatcher.Invoke(DispatcherPriority.SystemIdle, (Action)(() =>
				{
					// Text box Updates
					ConsoleExecutionLog.AppendText(e.Activity.DisplayName + " " + ((ActivityStateRecord)e.Record).State + Environment.NewLine);
					ConsoleExecutionLog.AppendText($"******************{Environment.NewLine}");
					_textLineToSourceLocationMap.Add(_lineIndex, _wfElementToSourceLocationMap[e.Activity]);
					_lineIndex += 2;

					// Add a sleep so that the debug adornments are visible to the user
					Thread.Sleep(TimeSpan.FromMilliseconds(500));
				}));
			}
		}

		// Provide Debug Adornment on the selected Activity
		private void ConsoleExecutionLog_SelectionChanged(object sender, RoutedEventArgs e)
		{
			string text = ConsoleExecutionLog.Text;

			int index = 0;
			int lineClicked = 0;
			while (index < text.Length)
			{
				if (text[index] == '\n')
				{
					lineClicked++;
				}

				if (ConsoleExecutionLog.SelectionStart <= index)
				{
					break;
				}

				index++;
			}

			Dispatcher.Invoke(DispatcherPriority.Normal, (Action)(() =>
			{
				try
				{
					// Tell Debug Service that the Line Clicked is _______
					CustomWfDesigner.Instance.DebugManagerView.CurrentLocation = _textLineToSourceLocationMap[lineClicked];
				}
				catch (Exception)
				{
					// If the user clicks other than on the tracking records themselves.
					CustomWfDesigner.Instance.DebugManagerView.CurrentLocation = new SourceLocation(_currentWorkflowFile, 1, 1, 1, 10);
				}
			}));
		}

		private void ConsoleExecutionLog_TextChanged(object sender, TextChangedEventArgs e)
		{
			ConsoleExecutionLog.ScrollToEnd();
		}

		private void InitializeActivitiesToolbox()
		{
			try
			{
				// load System Activity Libraries into current domain; uncomment more if libraries below available on your system
				AppDomain.CurrentDomain.Load("System.Activities");
				AppDomain.CurrentDomain.Load("System.ServiceModel.Activities");
				AppDomain.CurrentDomain.Load("System.Activities.Core.Presentation");
				//AppDomain.CurrentDomain.Load("Microsoft.Workflow.Management");
				//AppDomain.CurrentDomain.Load("Microsoft.Activities.Extensions");
				//AppDomain.CurrentDomain.Load("Microsoft.Activities");
				//AppDomain.CurrentDomain.Load("Microsoft.Activities.Hosting");
				//AppDomain.CurrentDomain.Load("Microsoft.PowerShell.Utility.Activities");
				//AppDomain.CurrentDomain.Load("Microsoft.PowerShell.Security.Activities");
				//AppDomain.CurrentDomain.Load("Microsoft.PowerShell.Management.Activities");
				//AppDomain.CurrentDomain.Load("Microsoft.PowerShell.Diagnostics.Activities");
				//AppDomain.CurrentDomain.Load("Microsoft.Powershell.Core.Activities");
				//AppDomain.CurrentDomain.Load("Microsoft.PowerShell.Activities");

				// get all loaded assemblies
				IEnumerable<Assembly> appAssemblies = AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name);

				// check if assemblies contain activities
				int activitiesCount = 0;
				foreach (Assembly activityLibrary in appAssemblies)
				{
					var wfToolboxCategory = new ToolboxCategory(activityLibrary.GetName().Name);
					var actvities = from
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
										&& (activityType.GetConstructor(Type.EmptyTypes) != null)
										&& !activityType.Name.Contains('`') //optional, for extra cleanup
									orderby
										activityType.Name
									select
										new ToolboxItemWrapper(activityType);

					actvities.ToList().ForEach(wfToolboxCategory.Add);

					if (wfToolboxCategory.Tools.Count > 0)
					{
						_wfToolbox.Categories.Add(wfToolboxCategory);
						activitiesCount += wfToolboxCategory.Tools.Count;
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

				LabelStatusBar.Content = $"Loaded Activities: {activitiesCount.ToString()}";
				WfToolboxBorder.Child = _wfToolbox;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
		}

		private void WfExecutionCompleted(WorkflowApplicationCompletedEventArgs e)
		{
			// This is to remove the final debug adornment
			Dispatcher.Invoke(DispatcherPriority.Render, (Action)(() =>
			{
				CustomWfDesigner.Instance.DebugManagerView.CurrentLocation = new SourceLocation(_currentWorkflowFile, 1, 1, 1, 10);
			}));
		}

		// Show the Debug Adornment
		private void ShowDebug(SourceLocation srcLoc)
		{
			Dispatcher.Invoke(DispatcherPriority.Render, (Action)(() =>
			{
				CustomWfDesigner.Instance.DebugManagerView.CurrentLocation = srcLoc;
			}));
		}

		private Dictionary<string, Activity> BuildActivityIdToWfElementMap(Dictionary<object, SourceLocation> wfElementToSourceLocationMap)
		{
			Dictionary<string, Activity> map = new Dictionary<string, Activity>();

			foreach (object instance in wfElementToSourceLocationMap.Keys)
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
			object rootInstance = GetRootInstance();
			Dictionary<object, SourceLocation> sourceLocationMapping = new Dictionary<object, SourceLocation>();

			if (rootInstance != null)
			{
				Activity documentRootElement = GetRootWorkflowElement(rootInstance);

				SourceLocationProvider.CollectMapping(
					GetRootRuntimeWorkflowElement(),
					documentRootElement,
					sourceLocationMapping,
					CustomWfDesigner.Instance.Context.Items.GetValue<WorkflowFileItem>().LoadedFile);

				// Collect the mapping between the Model Item tree and its underlying source location
				SourceLocationProvider.CollectMapping(
					documentRootElement,
					documentRootElement,
					_designerSourceLocationMapping,
				   CustomWfDesigner.Instance.Context.Items.GetValue<WorkflowFileItem>().LoadedFile);
			}

			// Notify the DebuggerService of the new sourceLocationMapping.
			// When rootInstance == null, it'll just reset the mapping.
			// DebuggerService debuggerService = debuggerService as DebuggerService;
			var debuggerService = (DebuggerService)CustomWfDesigner.Instance.DebugManagerView;
			debuggerService.UpdateSourceLocations(_designerSourceLocationMapping);

			return sourceLocationMapping;
		}

		#region Helper Methods

		private object GetRootInstance()
		{
			ModelService modelService = CustomWfDesigner.Instance.Context.Services.GetService<ModelService>();
			return modelService?.Root.GetCurrentValue();
		}

		// Get root WorkflowElement.  Currently only handle when the object is ActivitySchemaType or WorkflowElement.
		// May return null if it does not know how to get the root activity.
		private Activity GetRootWorkflowElement(object rootModelObject)
		{
			Debug.Assert(rootModelObject != null, "Cannot pass null as rootModelObject");

			Activity rootWorkflowElement;
			IDebuggableWorkflowTree debuggableWorkflowTree = rootModelObject as IDebuggableWorkflowTree;
			if (debuggableWorkflowTree != null)
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
			CustomWfDesigner.Instance.Flush();
			MemoryStream workflowStream = new MemoryStream(Encoding.Default.GetBytes(CustomWfDesigner.Instance.Text));

			ActivityXamlServicesSettings settings = new ActivityXamlServicesSettings()
			{
				CompileExpressions = true
			};

			Activity root = ActivityXamlServices.Load(workflowStream, settings);
			WorkflowInspectionServices.CacheMetadata(root);

			IEnumerator<Activity> enumerator1 = WorkflowInspectionServices.GetActivities(root).GetEnumerator();
			// Get the first child of the x:class
			enumerator1.MoveNext();
			root = enumerator1.Current;

			return root;
		}

		private void ResetUI()
		{
			ConsoleExecutionLog.Clear();
			ConsoleOutput.Clear();
		}

		private void RegenerateSourceDebuggerMappings()
		{
			// Updating the mapping between Model item and Source Location before we run the workflow so that BP setting can re-use that information from the DesignerSourceLocationMapping.
			_designerSourceLocationMapping.Clear();
			_textLineToSourceLocationMap.Clear();
			_lineIndex = 0;
			_wfElementToSourceLocationMap = UpdateSourceLocationMappingInDebuggerService();
			_executionLog.ActivityIdToWorkflowElementMap = BuildActivityIdToWfElementMap(_wfElementToSourceLocationMap);
		}

		#endregion

		#region Commands Handlers - Executed - New, Open, Save, Run

		private void CmdExit(object sender, ExecutedRoutedEventArgs e)
		{
			Application.Current.Shutdown();
		}

		private void CmdWorkflowRun(object sender, ExecutedRoutedEventArgs e)
		{
			ResetUI();
			RegenerateSourceDebuggerMappings();

			// get workflow source from designer
			CustomWfDesigner.Instance.Flush();
			var workflowStream = new MemoryStream(Encoding.Default.GetBytes(CustomWfDesigner.Instance.Text));

			var settings = new ActivityXamlServicesSettings
			{
				CompileExpressions = true
			};

			var activityExecute = ActivityXamlServices.Load(workflowStream, settings) as DynamicActivity;

			// configure workflow application
			_wfApp = new WorkflowApplication(activityExecute);
			_wfApp.Extensions.Add(_executionLog);
			_wfApp.Completed = WfExecutionCompleted;

			// execute 
			_wfApp.Run();
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
				var dialogSave = new SaveFileDialog { Title = "Save Workflow", Filter = "Workflows (.xaml)|*.xaml" };
				if (dialogSave.ShowDialog() == true)
				{
					CustomWfDesigner.Instance.Save(dialogSave.FileName);
					_currentWorkflowFile = dialogSave.FileName;
				}
			}
			else
			{
				CustomWfDesigner.Instance.Save(_currentWorkflowFile);
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
				using (var file = new StreamReader(dialogOpen.FileName, true))
				{
					CreateNewWorkflowDesigner(() => CustomWfDesigner.NewInstance(dialogOpen.FileName), dialogOpen.FileName);
				}
			}
		}

		private void CreateNewWorkflowDesigner(Action createWfDesigner, string currentWorkflowFile = "")
		{
			CustomWfDesigner.Instance.ModelChanged -= WorkflowDesigner_OnModelChanged;

			_currentWorkflowFile = currentWorkflowFile;
			createWfDesigner();
			WfDesignerBorder.Child = CustomWfDesigner.Instance.View;
			WfPropertyBorder.Child = CustomWfDesigner.Instance.PropertyInspectorView;

			CustomWfDesigner.Instance.ModelChanged += WorkflowDesigner_OnModelChanged;

			ResetUI();
			RegenerateSourceDebuggerMappings();
		}

		private void CmdToggleBreakpoint(object sender, ExecutedRoutedEventArgs e)
		{
			var mi = CustomWfDesigner.Instance.Context.Items.GetValue<Selection>().PrimarySelection;
			if (!(mi?.GetCurrentValue() is Activity activity))
			{
				return;
			}

			var srcLoc = _designerSourceLocationMapping[activity];
			if (!_breakpointList.Contains(srcLoc))
			{
				CustomWfDesigner.Instance.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint(srcLoc, BreakpointTypes.Bounded | BreakpointTypes.Enabled);
				_breakpointList.Add(srcLoc);
			}
			else
			{
				CustomWfDesigner.Instance.Context.Services.GetService<IDesignerDebugView>().UpdateBreakpoint(srcLoc, BreakpointTypes.None);
				_breakpointList.Remove(srcLoc);
			}
		}

		#endregion

		#region INotify

		public event PropertyChangedEventHandler PropertyChanged;

		private void NotifyPropertyChanged(String propertyName)
		{
			var handler = PropertyChanged;
			handler?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		#endregion
	}
}
