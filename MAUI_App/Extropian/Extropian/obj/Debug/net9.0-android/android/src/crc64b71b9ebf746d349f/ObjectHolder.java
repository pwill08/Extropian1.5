package crc64b71b9ebf746d349f;


public class ObjectHolder
	extends java.lang.Object
	implements
		mono.android.IGCUserPeer
{
/** @hide */
	public static final String __md_methods;
	static {
		__md_methods = 
			"";
		mono.android.Runtime.register ("Plugin.CloudFirestore.ObjectHolder, Plugin.CloudFirestore", ObjectHolder.class, __md_methods);
	}

	public ObjectHolder ()
	{
		super ();
		if (getClass () == ObjectHolder.class) {
			mono.android.TypeManager.Activate ("Plugin.CloudFirestore.ObjectHolder, Plugin.CloudFirestore", "", this, new java.lang.Object[] {  });
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
