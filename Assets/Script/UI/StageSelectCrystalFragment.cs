using UnityEngine;
using System.Collections.Generic;

/// <summary>A fitted polygon cut from the final crystal, keeping its texture and pixel grid continuous.</summary>
[RequireComponent(typeof(UnityEngine.UI.Image))]
[DisallowMultipleComponent]
public sealed class StageSelectCrystalFragment : UnityEngine.UI.BaseMeshEffect
{
    [SerializeField, HideInInspector] private Vector2[] sourceVertices;
    private float outlineStrength;
    private List<PixelRun> pixelRuns;
    private Rect cachedRect;
    private Vector2 cachedGrid;
    private Sprite cachedSprite;
    public float OutlineStrength => outlineStrength;

    private struct PixelRun
    {
        public Rect position, uv;
        public Color tint;
    }

    public void SetAssemblyProgress(float flight)
    {
        float strength=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.92f,1,flight));
        if(Mathf.Approximately(outlineStrength,strength)) return;
        outlineStrength=strength;
        if(graphic!=null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(UnityEngine.UI.VertexHelper mesh)
    {
        if (!IsActive() || sourceVertices == null || sourceVertices.Length < 3) return;
        var image = (UnityEngine.UI.Image)graphic;
        if (image.sprite == null) return;

        Vector2 min = sourceVertices[0], max = min;
        foreach (var point in sourceVertices)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        Vector2 size = max - min;
        if (size.x <= 0 || size.y <= 0) return;
        // Shared edges must retain identical subpixel positions; rounding each piece separately opens seams.
        Rect rect = graphic.rectTransform.rect;
        Rect source = image.sprite.rect;
        Vector2 textureSize = new Vector2(image.sprite.texture.width, image.sprite.texture.height);
        mesh.Clear();
        if(outlineStrength>0)
        {
            Vector2 grid=new Vector2(192,112);
            if(image.material!=null && image.material.HasProperty("_PixelGrid"))
            {
                Vector4 pixels=image.material.GetVector("_PixelGrid");
                grid=new Vector2(Mathf.Max(1,pixels.x),Mathf.Max(1,pixels.y));
            }
            if(pixelRuns==null || cachedRect!=rect || cachedGrid!=grid || cachedSprite!=image.sprite)
            {
                CachePixels(rect,source,textureSize,min,size,grid);
                cachedRect=rect; cachedGrid=grid; cachedSprite=image.sprite;
            }
            foreach(var run in pixelRuns)
            {
                Color color=graphic.color*Color.Lerp(Color.white,run.tint,outlineStrength);
                int first=mesh.currentVertCount;
                mesh.AddVert(new Vector2(run.position.xMin,run.position.yMin),color,new Vector2(run.uv.xMin,run.uv.yMin));
                mesh.AddVert(new Vector2(run.position.xMin,run.position.yMax),color,new Vector2(run.uv.xMin,run.uv.yMax));
                mesh.AddVert(new Vector2(run.position.xMax,run.position.yMax),color,new Vector2(run.uv.xMax,run.uv.yMax));
                mesh.AddVert(new Vector2(run.position.xMax,run.position.yMin),color,new Vector2(run.uv.xMax,run.uv.yMin));
                mesh.AddTriangle(first,first+1,first+2); mesh.AddTriangle(first+2,first+3,first);
            }
            return;
        }
        foreach (var point in sourceVertices)
        {
            Vector2 position = rect.min + Vector2.Scale((point - min) / size, rect.size);
            Vector2 uv = (source.position + Vector2.Scale(point, source.size)) / textureSize;
            mesh.AddVert(position, graphic.color, uv);
        }
        for (int i = 1; i < sourceVertices.Length - 1; i++) mesh.AddTriangle(0, i, i + 1);
    }

    private void CachePixels(Rect rect,Rect source,Vector2 textureSize,Vector2 min,Vector2 size,Vector2 grid)
    {
        pixelRuns=new List<PixelRun>();
        var origin=Vector2Int.FloorToInt(Vector2.Scale(min,grid));
        var end=Vector2Int.CeilToInt(Vector2.Scale(min+size,grid));
        int width=end.x-origin.x, height=end.y-origin.y;
        var filled=new bool[width,height];
        for(int y=0;y<height;y++) for(int x=0;x<width;x++)
        {
            Vector2 center=new Vector2((origin.x+x+.5f)/grid.x,(origin.y+y+.5f)/grid.y);
            bool positive=false, negative=false;
            for(int p=0;p<sourceVertices.Length;p++)
            {
                Vector2 a=sourceVertices[p], b=sourceVertices[(p+1)%sourceVertices.Length];
                float cross=(b.x-a.x)*(center.y-a.y)-(b.y-a.y)*(center.x-a.x);
                positive|=cross>.000001f; negative|=cross<-.000001f;
            }
            filled[x,y]=!(positive&&negative);
        }
        bool Filled(int x,int y)=>x>=0&&x<width&&y>=0&&y<height&&filled[x,y];
        int Shade(int x,int y)
        {
            if(!Filled(x,y)) return 0;
            if(!Filled(x,y+1)||!Filled(x-1,y)) return 1;
            return !Filled(x,y-1)||!Filled(x+1,y) ? 2 : 3;
        }
        // Merge identical adjacent cells into strips; avoid a separate quad/material for every pixel.
        for(int y=0;y<height;y++) for(int x=0;x<width;)
        {
            int shade=Shade(x,y), first=x++;
            while(x<width&&Shade(x,y)==shade) x++;
            if(shade==0) continue;
            Vector2 low=new Vector2((origin.x+first)/grid.x,(origin.y+y)/grid.y);
            Vector2 high=new Vector2((origin.x+x)/grid.x,(origin.y+y+1)/grid.y);
            Vector2 localLow=rect.min+Vector2.Scale((low-min)/size,rect.size);
            Vector2 localHigh=rect.min+Vector2.Scale((high-min)/size,rect.size);
            Vector2 uvLow=(source.position+Vector2.Scale(low,source.size))/textureSize;
            Vector2 uvHigh=(source.position+Vector2.Scale(high,source.size))/textureSize;
            pixelRuns.Add(new PixelRun
            {
                position=Rect.MinMaxRect(localLow.x,localLow.y,localHigh.x,localHigh.y),
                uv=Rect.MinMaxRect(uvLow.x,uvLow.y,uvHigh.x,uvHigh.y),
                tint=shade==1 ? new Color(.62f,.30f,.52f,1) : shade==2 ? new Color(.28f,.10f,.26f,1) : Color.white
            });
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        pixelRuns=null;
        base.OnValidate();
    }
#endif
}
