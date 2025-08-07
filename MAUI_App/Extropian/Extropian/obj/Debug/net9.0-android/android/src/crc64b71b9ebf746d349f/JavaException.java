package crc64b71b9ebf746d349f;


public class JavaException
	extends java.lang.Exception
	implements
		mono.android.IGCUserPeer
{
/** @hide */
	public static final String __md_methods;
	static {
		__md_methods = 
			"";
		mono.android.Runtime.register ("Plugin.CloudFirestore.JavaException, Plugin.CloudFirestore", JavaException.class, __md_methods);
	}

	public JavaException ()
	{
		super ();
		if (getClass () == JavaException.class) {
			mono.android.TypeManager.Activate ("Plugin.CloudFirestore.JavaException, Plugin.CloudFirestore", "", this, new java.lang.Object[] {  });
		}
	}

	public JavaException (java.lang.String p0, java.lang.Throwable p1)
	{
		super (p0, p1);
		if (getClass () == JavaException.class) {
			mono.android.TypeManager.Activate ("Plugin.CloudFirestore.JavaException, Plugin.CloudFirestore", "System.String, System.Private.CoreLib:Java.Lang.Throwable, Mono.Android", this, new java.lang.Object[] { p0, p1 });
		}
	}

	public JavaException (java.lang.String p0)
	{
		super (p0);
		if (getClass () == JavaException.class) {
			mono.android.TypeManager.Activate ("Plugin.CloudFirestore.JavaException, Plugin.CloudFirestore", "System.String, System.Private.CoreLib", this, new java.lang.Object[] { p0 });
		}
	}

	public JavaException (java.lang.Throwable p0)
	{
		super (p0);
		if (getClass () == JavaException.class) {
			mono.android.TypeManager.Activate ("Plugin.CloudFirestore.JavaException, Plugin.CloudFirestore", "Java.Lang.Throwable, Mono.Android", this, new java.lang.Object[] { p0 });
		}
	}

	private java.util.ArrayList refList;
	public void monodroidAddReference (java.lang.Object obj)
	{
		if (refList == null)
			refList = new java.util.ArrayList ();
		refList.add (obj);
	}

	public void monodroidClearReferences ()
	{
		if (refList != null)
			refList.clear ();
	}
}
