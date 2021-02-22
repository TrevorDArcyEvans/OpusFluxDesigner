using System;
using System.Activities;
using System.Windows.Forms;

namespace MeetupWfIntro.MeetupActivityLibrary.Misc
{
	/// <summary>
	/// Custom Activity that displays in a MessageBox the Value of the InputData argument
	/// </summary>
	public sealed class ShowMessageBox : CodeActivity
	{
		#region Arguments

		public InArgument<Object> InputData { get; set; }

		#endregion

		/// <summary>
		/// Constructor
		/// </summary>
		public ShowMessageBox() : base()
		{
			DisplayName = "Message";
		}

		/// <summary>
		/// Execution Logic
		/// </summary>
		protected override void Execute(CodeActivityContext context)
		{
			MessageBox.Show(this.InputData.Get(context).ToString());
		}
	}
}
