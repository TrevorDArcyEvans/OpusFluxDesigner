using Microsoft.CSharp.Activities;
using RehostedWorkflowDesigner.CSharpExpressionEditor;
using RehostedWorkflowDesigner.VbExpressionEditor;
using System;
using System.Activities.Core.Presentation;
using System.Activities.Presentation;
using System.Activities.Presentation.View;

namespace RehostedWorkflowDesigner.Helpers
{
	/// <summary>
	/// Workflow Designer Wrapper
	/// </summary>
	internal static class CustomWfDesigner
	{
		private const string DefaultWorkflow = "defaultWorkflow.xaml";
		private const string DefaultWorkflowCSharp = "defaultWorkflowCSharp.xaml";

		private static WorkflowDesigner _wfDesigner;
		private static RoslynExpressionEditorService _expressionEditorService;
		private static VbExpressionEditorService _expressionEditorServiceVB;

		/// <summary>
		/// Gets the current WorkflowDesigner Instance
		/// </summary>
		public static WorkflowDesigner Instance
		{
			get
			{
				if (_wfDesigner == null)
				{
					NewInstance(DefaultWorkflow);
				}
				return _wfDesigner;
			}
		}

		/// <summary>
		/// Creates a new Workflow Designer instance (VB)
		/// </summary>
		/// <param name="sourceFile">Workflow FileName</param>
		public static void NewInstance(string sourceFile = DefaultWorkflow)
		{
			_wfDesigner = new WorkflowDesigner();
			_wfDesigner.Context.Services.GetService<DesignerConfigurationService>().TargetFrameworkName = new System.Runtime.Versioning.FrameworkName(".NETFramework", new Version(4, 5));
			_wfDesigner.Context.Services.GetService<DesignerConfigurationService>().LoadingFromUntrustedSourceEnabled = true;

			//associates all of the basic activities with their designers
			new DesignerMetadata().Register();

			//load Workflow Xaml
			_wfDesigner.Load(sourceFile);
		}

		/// <summary>
		/// Creates a new Workflow Designer instance (VB) with Intellisense
		/// </summary>
		/// <param name="sourceFile">Workflow FileName</param>
		public static void NewInstanceVB(string sourceFile = DefaultWorkflow)
		{
			_expressionEditorServiceVB = new VbExpressionEditorService();

			_wfDesigner = new WorkflowDesigner();
			_wfDesigner.Context.Services.GetService<DesignerConfigurationService>().TargetFrameworkName = new System.Runtime.Versioning.FrameworkName(".NETFramework", new Version(4, 5));
			_wfDesigner.Context.Services.GetService<DesignerConfigurationService>().LoadingFromUntrustedSourceEnabled = true;
			_wfDesigner.Context.Services.Publish<IExpressionEditorService>(_expressionEditorServiceVB);

			//associates all of the basic activities with their designers
			new DesignerMetadata().Register();

			//load Workflow Xaml
			_wfDesigner.Load(sourceFile);
		}

		/// <summary>
		/// Creates a new Workflow Designer instance with C# Expression Editor
		/// </summary>
		/// <param name="sourceFile">Workflow FileName</param>
		public static void NewInstanceCSharp(string sourceFile = DefaultWorkflowCSharp)
		{
			_expressionEditorService = new RoslynExpressionEditorService();
			ExpressionTextBox.RegisterExpressionActivityEditor(new CSharpValue<string>().Language, typeof(RoslynExpressionEditor), CSharpExpressionHelper.CreateExpressionFromString);

			_wfDesigner = new WorkflowDesigner();
			_wfDesigner.Context.Services.GetService<DesignerConfigurationService>().TargetFrameworkName = new System.Runtime.Versioning.FrameworkName(".NETFramework", new Version(4, 5));
			_wfDesigner.Context.Services.GetService<DesignerConfigurationService>().LoadingFromUntrustedSourceEnabled = true;
			_wfDesigner.Context.Services.Publish<IExpressionEditorService>(_expressionEditorService);

			//associates all of the basic activities with their designers
			new DesignerMetadata().Register();

			//load Workflow Xaml
			_wfDesigner.Load(sourceFile);
		}
	}
}
