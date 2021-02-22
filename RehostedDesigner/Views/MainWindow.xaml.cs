using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Activities;
using System.Activities.Presentation.Toolbox;
using System.Activities.Tracking;
using System.Reflection;
using System.IO;
using System.Activities.XamlIntegration;
using Microsoft.Win32;
using RehostedWorkflowDesigner.Helpers;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Threading;

namespace RehostedWorkflowDesigner.Views
{
	public partial class MainWindow : INotifyPropertyChanged
	{
		private const string All = "*";

		private WorkflowApplication _wfApp;
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

		private string _currentWorkflowFile = string.Empty;
		private readonly ConsoleWriter _consoleWriter = new ConsoleWriter();

		public MainWindow()
		{
			InitializeComponent();

			// load all available workflow activities from loaded assemblies 
			InitializeActivitiesToolbox();

			// initialize designer
			WfDesignerBorder.Child = CustomWfDesigner.Instance.View;
			WfPropertyBorder.Child = CustomWfDesigner.Instance.PropertyInspectorView;

			_consoleWriter.WriteEvent += ConsoleWriter_WriteEvent;
			_consoleWriter.WriteLineEvent += ConsoleWriter_WriteEvent;

			Console.SetOut(_consoleWriter);

			_executionLog.TrackingRecordReceived += ExecutionLog_OnTrackingRecordReceived;
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
			if (e.Record != null)
			{
				Debug.WriteLine($"<+=+=+=+> Activity Tracking Record Received for record: {e.Record} ");

				Dispatcher.Invoke(DispatcherPriority.SystemIdle, (Action)(() =>
				{
					// Text box Updates
					ConsoleExecutionLog.AppendText(((ActivityStateRecord)e.Record).State + Environment.NewLine);
					ConsoleExecutionLog.AppendText($"******************{Environment.NewLine}");
				}));
			}
		}

		private void ConsoleExecutionLog_TextChanged(object sender, TextChangedEventArgs e)
		{
			ConsoleExecutionLog.ScrollToEnd();
		}

		/// <summary>
		/// Retrieves all Workflow Activities from the loaded assemblies and inserts them into a ToolboxControl 
		/// </summary>
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

		/// <summary>
		/// Retrieve Workflow Execution Logs and Workflow Execution Outputs
		/// </summary>
		private void WfExecutionCompleted(WorkflowApplicationCompletedEventArgs e)
		{
			try
			{
				// retrieve & display execution output
				foreach (var item in e.Outputs)
				{
					ConsoleOutput.Dispatcher.Invoke(
						DispatcherPriority.Normal,
						new Action(
							delegate
							{
								ConsoleOutput.Text += string.Format("[{0}] {1}" + Environment.NewLine, item.Key, item.Value);
							}
					));
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.ToString());
			}
		}

		#region Commands Handlers - Executed - New, Open, Save, Run

		/// <summary>
		/// Creates a new Workflow Application instance and executes the Current Workflow
		/// </summary>
		private void CmdWorkflowRun(object sender, ExecutedRoutedEventArgs e)
		{
			// get workflow source from designer
			CustomWfDesigner.Instance.Flush();
			MemoryStream workflowStream = new MemoryStream(Encoding.Default.GetBytes(CustomWfDesigner.Instance.Text));

			ActivityXamlServicesSettings settings = new ActivityXamlServicesSettings()
			{
				CompileExpressions = true
			};

			DynamicActivity activityExecute = ActivityXamlServices.Load(workflowStream, settings) as DynamicActivity;

			// configure workflow application
			ConsoleExecutionLog.Clear();
			ConsoleOutput.Clear();
			_wfApp = new WorkflowApplication(activityExecute);
			_wfApp.Extensions.Add(_executionLog);
			_wfApp.Completed = WfExecutionCompleted;

			// execute 
			_wfApp.Run();
		}

		/// <summary>
		/// Stops the Current Workflow
		/// </summary>
		private void CmdWorkflowStop(object sender, ExecutedRoutedEventArgs e)
		{
			// manual stop
			_wfApp?.Abort("Stopped by User");
		}

		/// <summary>
		/// Save the current state of a Workflow
		/// </summary>
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

		/// <summary>
		/// Creates a new Workflow Designer instance and loads the Default Workflow 
		/// </summary>
		private void CmdWorkflowNew(object sender, ExecutedRoutedEventArgs e)
		{
			_currentWorkflowFile = string.Empty;
			CustomWfDesigner.NewInstance();
			WfDesignerBorder.Child = CustomWfDesigner.Instance.View;
			WfPropertyBorder.Child = CustomWfDesigner.Instance.PropertyInspectorView;
		}

		/// <summary>
		/// Creates a new Workflow Designer instance and loads the Default Workflow 
		/// </summary>
		private void CmdWorkflowNewVB(object sender, ExecutedRoutedEventArgs e)
		{
			_currentWorkflowFile = string.Empty;
			CustomWfDesigner.NewInstanceVB();
			WfDesignerBorder.Child = CustomWfDesigner.Instance.View;
			WfPropertyBorder.Child = CustomWfDesigner.Instance.PropertyInspectorView;
		}

		/// <summary>
		/// Creates a new Workflow Designer instance and loads the Default Workflow with C# Expression Editor
		/// </summary>
		private void CmdWorkflowNewCSharp(object sender, ExecutedRoutedEventArgs e)
		{
			_currentWorkflowFile = string.Empty;
			CustomWfDesigner.NewInstanceCSharp();
			WfDesignerBorder.Child = CustomWfDesigner.Instance.View;
			WfPropertyBorder.Child = CustomWfDesigner.Instance.PropertyInspectorView;
		}

		/// <summary>
		/// Loads a Workflow into a new Workflow Designer instance
		/// </summary>
		private void CmdWorkflowOpen(object sender, ExecutedRoutedEventArgs e)
		{
			var dialogOpen = new OpenFileDialog { Title = "Open Workflow", Filter = "Workflows (.xaml)|*.xaml" };

			if (dialogOpen.ShowDialog() == true)
			{
				using (var file = new StreamReader(dialogOpen.FileName, true))
				{
					CustomWfDesigner.NewInstance(dialogOpen.FileName);
					WfDesignerBorder.Child = CustomWfDesigner.Instance.View;
					WfPropertyBorder.Child = CustomWfDesigner.Instance.PropertyInspectorView;

					_currentWorkflowFile = dialogOpen.FileName;
				}
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
