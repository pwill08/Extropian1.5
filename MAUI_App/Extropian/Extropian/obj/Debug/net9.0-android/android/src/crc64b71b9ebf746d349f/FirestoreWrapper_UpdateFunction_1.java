package crc64b71b9ebf746d349f;


public class FirestoreWrapper_UpdateFunction_1
	extends java.lang.Object
	implements
		mono.android.IGCUserPeer,
		com.google.firebase.firestore.Transaction.Function
{
/** @hide */
	public static final String __md_methods;
	static {
		__md_methods = 
			"n_apply:(Lcom/google/firebase/firestore/Transaction;)Ljava/lang/Object;:GetApply_Lcom_google_firebase_firestore_Transaction_Handler:Firebase.Firestore.Transaction/IFunctionInvoker, Xamarin.Firebase.Firestore\n" +
			"";
		mono.android.Runtime.register ("Plugin.CloudFirestore.FirestoreWrapper+UpdateFunction`1, Plugin.CloudFirestore", FirestoreWrapper_UpdateFunction_1.class, __md_methods);
	}

	public FirestoreWrapper_UpdateFunction_1 ()
	{
		super ();
		if (getClass () == FirestoreWrapper_UpdateFunction_1.class) {
			mono.android.TypeManager.Activate ("Plugin.CloudFirestore.FirestoreWrapper+UpdateFunction`1, Plugin.CloudFirestore", "", this, new java.lang.Object[] {  });
		}
	}

	public java.lang.Object apply (com.google.firebase.firestore.Transaction p0)
	{
		return n_apply (p0);
	}

	private native java.lang.Object n_apply (com.google.firebase.firestore.Transaction p0);

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
