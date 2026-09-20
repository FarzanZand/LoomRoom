using System.IO;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

// Preview cameras are temporary; decoration itself is authored in the Room scene.
public static class ApartmentDecoration {
 [MenuItem("LoomRoom/Apartment/Render Decoration Views")]
 public static void RenderViews(){
 Render("kitchen",new Vector3(-532,34,320),new Vector3(-648,23,302));
 Render("bedroom",new Vector3(-530,34,445),new Vector3(-625,22,509));
 Render("hobby",new Vector3(-435,34,300),new Vector3(-364,25,465));
 Render("hall",new Vector3(-495,34,252),new Vector3(-495,28,440));
 ApartmentLayoutRefinement.RenderViews();
 }
 static void Render(string name,Vector3 pos,Vector3 target){var g=new GameObject("Temporary decor preview");var c=g.AddComponent<Camera>();var rt=new RenderTexture(1600,1000,24);var prior=RenderTexture.active;try{c.transform.SetPositionAndRotation(pos,Quaternion.LookRotation(target-pos));c.fieldOfView=72;c.nearClipPlane=.3f;c.farClipPlane=1000;c.targetTexture=rt;c.Render();RenderTexture.active=rt;var tex=new Texture2D(1600,1000,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,1000),0,0);tex.Apply();File.WriteAllBytes("Temp/decor-"+name+".png",tex.EncodeToPNG());Object.DestroyImmediate(tex);}finally{RenderTexture.active=prior;Object.DestroyImmediate(g);rt.Release();Object.DestroyImmediate(rt);}}
}
