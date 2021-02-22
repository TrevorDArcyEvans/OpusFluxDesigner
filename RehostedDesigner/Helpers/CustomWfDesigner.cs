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
		private const string DefaultWorkflow = "DefaultWorkflow.xaml";
		private const string DefaultWorkflowCSharp = "DefaultWorkflowCSharp.xaml";

		private static readonly RoslynExpressionEditorService ExpressionEditorService = new RoslynExpressionEditorService();
		private static readonly VbExpressionEditorService ExpressionEditorServiceVb = new VbExpressionEditorService();

		private static WorkflowDesigner CreateInstance(string sourceFile = DefaultWorkflow)
		{
			var wfDesigner = new WorkflowDesigner();
			wfDesigner.Context.Services.GetService<DesignerConfigurationService>().TargetFrameworkName = new System.Runtime.Versioning.FrameworkName(".NETFramework", new Version(4, 5));
			wfDesigner.Context.Services.GetService<DesignerConfigurationService>().LoadingFromUntrustedSourceEnabled = true;

			// associates all of the basic activities with their designers
			new DesignerMetadata().Register();

			// load Workflow Xaml
			wfDesigner.Load(sourceFile);

			return wfDesigner;
		}

		/// <summary>
		/// Creates a new Workflow Designer instance (VB)
		/// </summary>
		/// <param name="sourceFile">Workflow FileName</param>
		public static WorkflowDesigner NewInstance(string sourceFile = DefaultWorkflow)
		{
			return CreateInstance(sourceFile);
		}

		/// <summary>
		/// Creates a new Workflow Designer instance (VB) with Intellisense
		/// </summary>
		/// <param name="sourceFile">Workflow FileName</param>
		public static WorkflowDesigner NewInstanceVB(string sourceFile = DefaultWorkflow)
		{
			var wfDesigner = CreateInstance(sourceFile);
			wfDesigner.Context.Services.Publish<IExpressionEditorService>(ExpressionEditorServiceVb);

			return wfDesigner;
		}

		/// <summary>
		/// Creates a new Workflow Designer instance with C# Expression Editor
		/// </summary>
		/// <param name="sourceFile">Workflow FileName</param>
		public static WorkflowDesigner NewInstanceCSharp(string sourceFile = DefaultWorkflowCSharp)
		{
			ExpressionTextBox.RegisterExpressionActivityEditor(new CSharpValue<string>().Language, typeof(RoslynExpressionEditor), CSharpExpressionHelper.CreateExpressionFromString);

			var wfDesigner = CreateInstance(sourceFile);
			wfDesigner.Context.Services.Publish<IExpressionEditorService>(ExpressionEditorService);

			return wfDesigner;
		}
	}
}
