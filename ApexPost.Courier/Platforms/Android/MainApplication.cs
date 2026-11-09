using Android.App;
using Android.Runtime;
using Couchbase.Lite.Support;

namespace ApexPost.Courier;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	public override void OnCreate()
	{
		// Couchbase Lite on Android needs the application context before its first use, so the database can find
		// its native libraries and its files folder.
		Droid.Activate(this);
		base.OnCreate();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
