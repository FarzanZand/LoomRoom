using UnityEngine;

// The ways in and out of a floor: the ladder under the entrance hatch and the stairwell (or the
// final ladder) at the exit.
public partial class DungeonGenerator
{
    bool Descends=>FloorNumber<data.FloorCount;
    DungeonExit MakeExit(Vector3 pos,bool entrance)
    {
        bool descending=!entrance && Descends;
        var go=new GameObject(entrance ? "Entrance stair" : descending ? "Stairs down" : "Final exit stair"); go.transform.SetParent(transform,false); go.transform.position=pos;
        var exit=go.AddComponent<DungeonExit>(); exit.entrance=entrance;
        var col=go.AddComponent<BoxCollider>(); col.isTrigger=true;
        // On a board the way on is always the stone stairwell, and there is no ladder to climb in by.
        if(descending || (Board && !entrance)){ BuildStairwell(go.transform,exit); col.center=new Vector3(0,1f,.3f); col.size=new Vector3(1.1f,1.6f,1f); }
        else { if(!Board) BuildLadder(go.transform); col.center=new Vector3(0,1,0); col.size=new Vector3(1.2f,2,1.2f); }
        go.AddComponent<InteractableTrigger>();
        return exit;
    }

    // A stone stairwell: up onto a landing, then steps going down into the dark under a lintel.
    // (The dungeon sits on the table top, so the way down is built above the floor.) Faces the
    // open neighbouring cell; an iron portcullis closes it while the exit is sealed.
    static readonly Vector2Int[] StairwellFacings={Vector2Int.down,Vector2Int.left,Vector2Int.right,Vector2Int.up};
    void BuildStairwell(Transform root, DungeonExit exit)
    {
        var cell=CellOf(root.position);
        foreach(var d in StairwellFacings)
            if(Layout.InBounds(cell+d) && Layout.floor[cell.x+d.x,cell.y+d.y]){ root.rotation=Quaternion.LookRotation(new Vector3(-d.x,0,-d.y)); break; }
        int region=Layout.RegionIds[cell.x,cell.y];
        var stone=FloorMaterial(region,cell.x,cell.y);
        var walls=stone;
        var block=new MaterialPropertyBlock();
        GameObject Part(string name,Vector3 local,Vector3 size,Material material,float shade=1,Transform parent=null)
        {
            var g=Box(name,root.TransformPoint(local),size,material,parent!=null ? parent : root); g.transform.rotation=root.rotation;
            if(shade<1){ var r=g.GetComponent<Renderer>(); r.GetPropertyBlock(block); var c=Color.white*shade; c.a=1; block.SetColor("_BaseColor",c); block.SetColor("_Color",c); r.SetPropertyBlock(block); }
            return g;
        }
        // Local +z points into the stairwell; the opening faces -z.
        const float width=1.1f, height=2.4f, wall=.28f, back=.95f;
        // Two steps up to the landing at the mouth.
        Part("Step",new Vector3(0,.06f,-.75f),new Vector3(width+.3f,.12f,.3f),data.trimMaterial);
        Part("Landing",new Vector3(0,.12f,-.42f),new Vector3(width+.3f,.24f,.4f),data.trimMaterial);
        // Inside: steps going down, darker the deeper they go, ending in black.
        for(int i=0;i<4;i++)
        {
            float top=.18f-i*.055f, z=-.09f+i*.25f;
            Part("Step down",new Vector3(0,top*.5f,z),new Vector3(width,top,.25f),stone,Mathf.Lerp(.55f,.04f,i/3f));
        }
        Part("Dark",new Vector3(0,height*.5f,back-.05f),new Vector3(width,height,.1f),walls,0);
        Part("Dark roof",new Vector3(0,height-.05f,.4f),new Vector3(width,.1f,1.2f),walls,0);
        // Stone housing.
        foreach(float x in new[]{-1f,1f})
        {
            Part("Side wall",new Vector3(x*(width+wall)*.5f,height*.5f,.35f),new Vector3(wall,height,1.3f),walls);
            Part("Side wall inner",new Vector3(x*(width*.5f-.01f),height*.5f,.45f),new Vector3(.02f,height,1f),walls,.08f);
        }
        Part("Back wall",new Vector3(0,height*.5f,back+wall*.5f),new Vector3(width+wall*2,height,wall),walls);
        Part("Lintel",new Vector3(0,height+.1f,-.25f),new Vector3(width+wall*2+.12f,.22f,.3f),data.trimMaterial);
        Part("Roof",new Vector3(0,height+.06f,.4f),new Vector3(width+wall*2+.06f,.14f,1.35f),walls);
        // Portcullis while a guardian lives.
        var gate=new GameObject("Portcullis"); gate.transform.SetParent(root,false);
        for(int i=-2;i<=2;i++) Part("Bar",new Vector3(i*.22f,height*.5f,-.2f),new Vector3(.05f,height,.05f),data.metalMaterial,1,gate.transform);
        for(int i=1;i<=3;i++) Part("Bar",new Vector3(0,i*.45f,-.2f),new Vector3(width,.05f,.05f),data.metalMaterial,1,gate.transform);
        var bars=gate.AddComponent<BoxCollider>(); bars.center=new Vector3(0,height*.5f,-.2f); bars.size=new Vector3(width,height,.1f);
        exit.grate=gate; gate.SetActive(exit.Sealed);
    }

    // A wooden ladder up to a hatch in the ceiling: the way in, and the way out on the last floor.
    void BuildLadder(Transform root)
    {
        var cell=CellOf(root.position);
        float height=Layout.InBounds(cell) && Layout.floor[cell.x,cell.y] ? HeightAt(cell.x,cell.y) : data.architectureTileSize;
        var p=root.position;
        var wood=data.woodMaterial;
        foreach(float x in new[]{-.28f,.28f}) Box("Rail",p+new Vector3(x,height*.5f,0),new Vector3(.08f,height,.08f),wood,root);
        for(float y=.3f;y<height-.1f;y+=.36f) Box("Rung",p+new Vector3(0,y,0),new Vector3(.52f,.05f,.06f),wood,root);
        // Hatch: a dark square framed in wood, just under the ceiling.
        Box("Hatch",p+Vector3.up*(height-.05f),new Vector3(1.1f,.06f,1.1f),wood,root);
        foreach(var d in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
            Box("Hatch frame",p+d*.6f+Vector3.up*(height-.07f),d.x!=0 ? new Vector3(.12f,.14f,1.32f) : new Vector3(1.32f,.14f,.12f),data.trimMaterial,root);
    }
}
