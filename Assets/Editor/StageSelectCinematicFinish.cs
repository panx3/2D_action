using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Finishes the existing cartridge theatre in place; never touches gameplay or progress assets.</summary>
public static class StageSelectCinematicFinish
{
    private const string Art = "Assets/Art/StageSelect/Cinematic/";

    public static string Apply(bool groundingOnly = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var previous = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(StageSelectSceneBuilder.ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(StageSelectSceneBuilder.ScenePath, OpenSceneMode.Additive);
        try
        {
            if (scene.isDirty) throw new InvalidOperationException("StageSelectScene contains unsaved edits; finish not applied.");
            if (groundingOnly)
            {
                ApplyGrounding(scene);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                return "Saved only the requested room/grounding/cable/silent-BGM revision to StageSelectScene.";
            }
            var root = scene.GetRootGameObjects().Single(g => g.GetComponent<StageSelectController>() != null);
            RectTransform Find(string name) => root.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == name);
            var world = Find("Theatre");
            var card = Find("Cartridge");
            var controls = Find("Controls");
            var tv = Find("Television").GetComponent<UnityEngine.UI.Image>();
            var preview = root.GetComponent<StageSelectPreview>();
            var controller = root.GetComponent<StageSelectController>();
            var lighting = root.GetComponent<StageSelectLighting>();
            ImportRoom();
            var backdrop = Find("DarkBackdrop").GetComponent<UnityEngine.UI.Image>();
            backdrop.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "TheatreRoom.png");
            backdrop.material = null; backdrop.color = Color.white;
            Set(Find("BackgroundCRT"), new Vector2(0,107), new Vector2(684,400));
            Set(tv.rectTransform, new Vector2(0,90), new Vector2(780,534));
            var housing = CopyMaterial(tv.material, "TheatreHousing");
            housing.SetVector("_HousingSize", new Vector4(780,534,684,400));
            housing.SetVector("_FaceOffset", new Vector4(0,17,0,0));
            housing.SetFloat("_Relight", .8f); Save(housing); tv.material = housing;

            var shell = Find("Shell").GetComponent<UnityEngine.UI.Image>();
            var shellMaterial = CopyMaterial(shell.material, "CartridgeFinish");
            shellMaterial.SetFloat("_Relight", .65f); Save(shellMaterial); shell.material = shellMaterial;
            var label = Raw("PrintedStageArtwork", card, new Vector2(0,29), new Vector2(338,100), Atmosphere("PrintedArtwork",3));
            label.transform.SetSiblingIndex(shell.transform.GetSiblingIndex()+1);
            Set(Find("StageName"),new Vector2(0,29),new Vector2(327,70));
            Set(Find("StageNameText"),new Vector2(0,29),new Vector2(327,70));
            var sheen = Image("LabelCatchlight",card,new Vector2(0,29),new Vector2(338,100),Atmosphere("LabelCatchlight",5));
            sheen.color = Color.clear;

            var reflection = Raw("CRTGlassReflection",world,new Vector2(0,-285),new Vector2(820,146),Atmosphere("CRTReflection",0));
            reflection.transform.SetSiblingIndex(Find("Television").GetSiblingIndex()+1);
            var consoleReflection = Raw("ConsoleFloorReflection",world,new Vector2(0,-345),new Vector2(660,80),Atmosphere("ConsoleReflection",4));
            consoleReflection.texture = Find("Console").GetComponent<UnityEngine.UI.Image>().sprite.texture;
            consoleReflection.color = new Color(.9f,.8f,.92f,.55f);
            consoleReflection.transform.SetSiblingIndex(reflection.transform.GetSiblingIndex()+1);
            var contact = Find("ConsoleContactShadow");
            contact.SetSiblingIndex(Find("ConsoleBackDeck").GetSiblingIndex());
            var veil = Image("UnlockFocusVeil",world,Vector2.zero,new Vector2(1600,1000),null);
            veil.color = Color.clear; veil.transform.SetSiblingIndex(card.GetSiblingIndex());
            var halo = Image("UnlockEnergy",world,card.anchoredPosition,new Vector2(960,590),Atmosphere("UnlockEnergy",2));
            halo.color = Color.clear; halo.transform.SetSiblingIndex(card.GetSiblingIndex());
            var vignette = Image("LensVignette",world,Vector2.zero,new Vector2(1280,720),Atmosphere("LensVignette",1));
            vignette.color = new Color(1,1,1,.52f);
            vignette.transform.SetSiblingIndex(controls.GetSiblingIndex());

            var arrowHalos = new UnityEngine.UI.Image[2];
            for (int i = 0; i < 2; i++)
            {
                var arrow = Find(i==0 ? "Previous" : "Next");
                arrow.anchoredPosition = new Vector2(i==0 ? -404 : 404,65);
                arrow.sizeDelta = new Vector2(40,56);
                arrowHalos[i] = Image("ArrowBloom"+i,arrow,Vector2.zero,new Vector2(115,120),
                    AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/StageSelect/DarkRetro/LightSpill.mat"));
                arrowHalos[i].transform.SetAsFirstSibling();
            }
            var status = Find("Availability").GetComponent<TMP_Text>();
            Set(status.rectTransform,new Vector2(0,-319),new Vector2(600,24));
            status.fontSize=13; status.alignment=TextAlignmentOptions.Center;
            var hint = Find("InputHints").GetComponent<TMP_Text>();
            Set(hint.rectTransform,new Vector2(0,-346),new Vector2(1000,24));
            hint.fontSize=13; hint.color=new Color(.65f,.67f,.76f);
            var index = Label("CartridgeIndex",controls,new Vector2(514,-319),new Vector2(170,24),hint,13);
            index.text="01  /  02"; index.characterSpacing=3;
            var announcement = Rect("UnlockAnnouncement",world,new Vector2(0,-94),new Vector2(720,44));
            var announcementGroup = announcement.GetComponent<CanvasGroup>();
            if (announcementGroup == null) announcementGroup = announcement.gameObject.AddComponent<CanvasGroup>();
            announcementGroup.alpha=0; announcementGroup.blocksRaycasts=false; announcementGroup.interactable=false;
            var announcementText = Label("UnlockAnnouncementText",announcement,Vector2.zero,new Vector2(720,34),hint,15);
            announcementText.color=new Color(1,.84f,.7f); announcementText.characterSpacing=4;

            var motes = new UnityEngine.UI.Image[10];
            for (int i = 0; i < motes.Length; i++)
            {
                var rect=Find("AmbientCrystal_"+i); rect.gameObject.SetActive(true);
                rect.anchoredPosition = new Vector2((i%2==0?-1:1)*(292+i%4*37),-70-i/2*34);
                rect.sizeDelta = new Vector2(25+i%3*7,29+i%3*8);
                motes[i]=rect.GetComponent<UnityEngine.UI.Image>();
            }
            var variants=AssetDatabase.LoadAllAssetsAtPath(Art+"CrystalShards.png").OfType<Sprite>().OrderBy(s=>s.name).ToArray();
            if(variants.Length!=8) throw new InvalidOperationException("Import all eight crystal shard variants before applying.");
            var shardMaterial=CopyMaterial(Find("Shard_0").GetComponent<UnityEngine.UI.Image>().material,"CrystalShardFinish");
            // Atlas coordinates must stay exact: quantisation belongs in the source pixel art.
            shardMaterial.SetVector("_PixelGrid",new Vector4(variants[0].texture.width,variants[0].texture.height,0,0));
            shardMaterial.SetFloat("_Relight",.45f); Save(shardMaterial);
            var shardImages=new UnityEngine.UI.Image[24];
            for(int i=0;i<shardImages.Length;i++)
            {
                var image=Find("Shard_"+i).GetComponent<UnityEngine.UI.Image>();
                image.sprite=variants[i%variants.Length]; image.material=shardMaterial;
                image.preserveAspect=true; image.rectTransform.sizeDelta=Vector2.one*(54+i%5*12);
                shardImages[i]=image;
            }
            Edit(preview,so=>
            {
                Ref(so,"cartridgeArtwork",label); Ref(so,"floorReflection",reflection);
                so.FindProperty("softness").floatValue=.65f;
                so.FindProperty("scanlines").floatValue=.17f;
                so.FindProperty("rgbSeparation").floatValue=.55f;
                so.FindProperty("brightness").floatValue=1.02f;
                so.FindProperty("glassReflection").floatValue=.21f;
                so.FindProperty("phosphorBloom").floatValue=.28f;
            });
            Edit(controller,so=>
            {
                Ref(so,"stageNumber",index); Ref(so,"unlockLabel",announcementText);
                Ref(so,"unlockAnnouncement",announcementGroup); Ref(so,"labelSheen",sheen);
                so.FindProperty("gatherDuration").floatValue=2.15f;
                so.FindProperty("chargeHoldDuration").floatValue=.3f;
                so.FindProperty("crackDuration").floatValue=.68f;
                so.FindProperty("burstDuration").floatValue=.78f;
                so.FindProperty("unlockSettleDuration").floatValue=1.55f;
                so.FindProperty("switchDuration").floatValue=.42f;
                so.FindProperty("alignDuration").floatValue=.25f;
                so.FindProperty("insertDuration").floatValue=.5f;
                so.FindProperty("bootDuration").floatValue=.48f;
                so.FindProperty("zoomDuration").floatValue=1.45f;
                so.FindProperty("revealDuration").floatValue=.65f;
                so.FindProperty("floatHeight").floatValue=3;
                so.FindProperty("floatPeriod").floatValue=4.8f;
            });
            Edit(lighting,so=>
            {
                Ref(so,"focusVeil",veil); Ref(so,"energyHalo",halo); Ref(so,"reflectedScreen",reflection);
                Refs(so,"arrowHalos",arrowHalos); Refs(so,"shardImages",shardImages); Refs(so,"motes",motes);
                so.FindProperty("crtSpill").floatValue=.16f;
                so.FindProperty("floorReflection").floatValue=.12f;
                so.FindProperty("moteOpacity").floatValue=.56f;
            });
            ConfigureAssembly(root);
            ApplyGrounding(scene);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return "Saved cinematic finish to StageSelectScene only: room, printed labels, reflections, layered light, eight shard variants and paced unlock/entry.";
        }
        finally
        {
            if(previous.IsValid()&&previous.isLoaded) SceneManager.SetActiveScene(previous);
            if(opened) EditorSceneManager.CloseScene(scene,true);
        }
    }
    public static string ApplyCrystalAssembly(bool correctGlassInset = false)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var previous=SceneManager.GetActiveScene();
        var scene=SceneManager.GetSceneByPath(StageSelectSceneBuilder.ScenePath);
        bool opened=!scene.isLoaded;
        if(opened) scene=EditorSceneManager.OpenScene(StageSelectSceneBuilder.ScenePath,OpenSceneMode.Additive);
        try
        {
            if(scene.isDirty) throw new InvalidOperationException("StageSelectScene contains unsaved edits; assembly not applied.");
            var root=scene.GetRootGameObjects().Single(g=>g.GetComponent<StageSelectController>()!=null);
            ConfigureAssembly(root);
            if(correctGlassInset) ConfigureGlassInset(root);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            return "Saved accumulating pixel-art crystal pile and light-covered final-image swap"+(correctGlassInset ? "; corrected CRT top inset" : "")+"; other settings preserved.";
        }
        finally
        {
            if(previous.IsValid()&&previous.isLoaded) SceneManager.SetActiveScene(previous);
            if(opened) EditorSceneManager.CloseScene(scene,true);
        }
    }
    private static void ConfigureAssembly(GameObject root)
    {
        RectTransform Find(string name)=>root.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name==name);
        var finalImage=Find("ExistingCrystal").GetComponent<UnityEngine.UI.Image>();
        var source=finalImage.sprite;
        if(source==null) throw new InvalidOperationException("The final cassette crystal is required.");
        var variants=AssetDatabase.LoadAllAssetsAtPath(Art+"CrystalShards.png").OfType<Sprite>().OrderBy(s=>s.name).ToArray();
        if(variants.Length!=8) throw new InvalidOperationException("Eight dimensional pixel-art shards are required.");
        var material=AssetDatabase.LoadAssetAtPath<Material>(Art+"CrystalShardFinish.mat");
        if(material==null) throw new InvalidOperationException("The existing shard material is required.");
        Vector2 fullSize=finalImage.rectTransform.rect.size;
        float fit=Mathf.Min(fullSize.x/source.rect.width,fullSize.y/source.rect.height);
        fullSize=source.rect.size*fit;
        var pieces=BuildCrystalPile(source,variants,fullSize);
        var maskImage=Image("CrystalPile",Find("Theatre"),Find("Cartridge").anchoredPosition,finalImage.rectTransform.sizeDelta,finalImage.material);
        maskImage.sprite=source; maskImage.preserveAspect=true;
        var mask=maskImage.GetComponent<UnityEngine.UI.Mask>();
        if(mask==null) mask=maskImage.gameObject.AddComponent<UnityEngine.UI.Mask>();
        mask.showMaskGraphic=false;
        var group=maskImage.GetComponent<CanvasGroup>();
        if(group==null) group=maskImage.gameObject.AddComponent<CanvasGroup>();
        int flightLayer=Find("CrystalFragments").GetSiblingIndex();
        maskImage.transform.SetSiblingIndex(flightLayer-(maskImage.transform.GetSiblingIndex()<flightLayer ? 1 : 0));
        var images=new UnityEngine.UI.Image[pieces.Count];
        var rects=new RectTransform[images.Length];
        var landed=new RectTransform[images.Length];
        var positions=new Vector2[images.Length];
        var angles=new float[images.Length];
        for(int i=0;i<images.Length;i++)
        {
            var piece=pieces[i]; positions[i]=piece.position; angles[i]=piece.angle;
            images[i]=Image("Shard_"+i,Find("CrystalFragments"),Vector2.zero,Vector2.one*piece.size,material);
            var fragment=images[i].GetComponent<StageSelectCrystalFragment>();
            if(fragment!=null) UnityEngine.Object.DestroyImmediate(fragment);
            images[i].sprite=piece.sprite; images[i].preserveAspect=true;
            images[i].SetVerticesDirty();
            rects[i]=images[i].rectTransform;
            var settled=Image("EmbeddedShard_"+i,maskImage.transform,piece.position,Vector2.one*piece.size,material);
            settled.sprite=piece.sprite; settled.preserveAspect=true;
            landed[i]=settled.rectTransform;
            landed[i].localEulerAngles=new Vector3(0,0,piece.angle);
            landed[i].gameObject.SetActive(false);
        }
        Edit(root.GetComponent<StageSelectController>(),so=>
        {
            Refs(so,"shards",rects);
            Refs(so,"piledShards",landed); Ref(so,"crystalPileGroup",group);
            var targets=so.FindProperty("assemblyPositions"); targets.arraySize=positions.Length;
            for(int i=0;i<positions.Length;i++) targets.GetArrayElementAtIndex(i).vector2Value=positions[i];
            var rotations=so.FindProperty("assemblyAngles"); rotations.arraySize=angles.Length;
            for(int i=0;i<angles.Length;i++) rotations.GetArrayElementAtIndex(i).floatValue=angles[i];
            so.FindProperty("gatherDuration").floatValue=3.6f;
            so.FindProperty("chargeHoldDuration").floatValue=.55f;
            so.FindProperty("foregroundShardScale").floatValue=1.2f;
            so.FindProperty("assembledShardScale").floatValue=1f;
        });
        group.gameObject.SetActive(false);
        Edit(root.GetComponent<StageSelectLighting>(),so=>Refs(so,"shardImages",images));
        // The rejected rectangular placeholder has no remaining serialized/runtime references.
        var obsolete=Find("Theatre").Find("CrystalAssembly");
        if(obsolete!=null) UnityEngine.Object.DestroyImmediate(obsolete.gameObject);
    }

    private struct CrystalPiece
    {
        public Sprite sprite;
        public Vector2 position;
        public float size, angle;
    }

    private static List<CrystalPiece> BuildCrystalPile(Sprite source,Sprite[] variants,Vector2 fullSize)
    {
        // Plan against actual opaque pixels. Irregular sprites must overlap enough to leave no holes in the final silhouette.
        var silhouette=new Texture2D(2,2,TextureFormat.RGBA32,false);
        var atlas=new Texture2D(2,2,TextureFormat.RGBA32,false);
        try
        {
            silhouette.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(source.texture)));
            atlas.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(variants[0].texture)));
            var outline= silhouette.GetPixels32(); var pixels=atlas.GetPixels32();
            const int width=192,height=112;
            var needed=new bool[width*height]; var covered=new bool[needed.Length];
            var samples=new Vector2[needed.Length];
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                int n=y*width+x;
                Vector2 uv=new Vector2((x+.5f)/width,(y+.5f)/height);
                samples[n]=Vector2.Scale(uv-Vector2.one*.5f,fullSize);
                int tx=Mathf.Clamp(Mathf.FloorToInt(source.rect.x+uv.x*source.rect.width),0,silhouette.width-1);
                int ty=Mathf.Clamp(Mathf.FloorToInt(source.rect.y+uv.y*source.rect.height),0,silhouette.height-1);
                needed[n]=outline[ty*silhouette.width+tx].a>64;
            }
            byte Alpha(Sprite sprite,Vector2 uv)
            {
                if(uv.x<0||uv.x>=1||uv.y<0||uv.y>=1) return 0;
                int x=Mathf.Clamp(Mathf.FloorToInt(sprite.rect.x+uv.x*sprite.rect.width),0,atlas.width-1);
                int y=Mathf.Clamp(Mathf.FloorToInt(sprite.rect.y+uv.y*sprite.rect.height),0,atlas.height-1);
                return pixels[y*atlas.width+x].a;
            }
            var anchors=new Vector2[variants.Length];
            for(int v=0;v<variants.Length;v++)
            {
                float best=float.MaxValue;
                for(int y=0;y<64;y++) for(int x=0;x<64;x++)
                {
                    Vector2 uv=new Vector2((x+.5f)/64,(y+.5f)/64);
                    float score=(uv-Vector2.one*.5f).sqrMagnitude;
                    if(score<best&&Alpha(variants[v],uv)>224) { best=score; anchors[v]=uv; }
                }
                if(best==float.MaxValue) throw new InvalidOperationException("Shard has no opaque core: "+variants[v].name);
            }
            var pieces=new List<CrystalPiece>();
            void Add(Vector2 point,int variant,float size,float angle)
            {
                Quaternion rotation=Quaternion.Euler(0,0,angle);
                Vector2 position=point-(Vector2)(rotation*((anchors[variant]-Vector2.one*.5f)*size));
                var piece=new CrystalPiece { sprite=variants[variant],position=position,size=size,angle=angle };
                pieces.Add(piece);
                Quaternion inverse=Quaternion.Inverse(rotation);
                for(int p=0;p<samples.Length;p++) if(needed[p]&&!covered[p])
                {
                    Vector2 uv=(Vector2)(inverse*(samples[p]-position))/size+Vector2.one*.5f;
                    if(Alpha(piece.sprite,uv)>224) covered[p]=true;
                }
            }
            int[] pattern={4,2,6,1,4,2,6,7,4,2,6,1,0,3,5,4};
            for(int y=0;y<8;y++) for(int x=0;x<12;x++)
            {
                int i=y*12+x;
                var position=Vector2.Scale(new Vector2((x/11f-.5f)*.94f,(y/7f-.5f)*.9f),fullSize);
                Add(position,pattern[i%pattern.Length],84+i%4*4,((i*7)%9-4)*7.5f+(i%3==0?180:0));
            }
            // Close residual cavities with real shaded chunks, rather than a solid rectangle or a final-image backing.
            int hole;
            int[] solidVariants={4,2,6};
            for(int pass=0;pass<160;pass++)
            {
                hole=-1;
                for(int p=0;p<needed.Length;p++) if(needed[p]&&!covered[p]) { hole=p; break; }
                if(hole<0) break;
                Add(samples[hole],solidVariants[pass%solidVariants.Length],88,pass*137.5f);
            }
            if(Enumerable.Range(0,needed.Length).Any(p=>needed[p]&&!covered[p]))
                throw new InvalidOperationException("Crystal pile could not cover the cassette silhouette.");
            return pieces.OrderBy(p=>Mathf.Max(Mathf.Abs(p.position.x)/fullSize.x,Mathf.Abs(p.position.y)/fullSize.y))
                .ThenBy(p=>Mathf.Atan2(p.position.y,p.position.x)).ToList();
        }
        finally { UnityEngine.Object.DestroyImmediate(silhouette); UnityEngine.Object.DestroyImmediate(atlas); }
    }
    private static void ConfigureGlassInset(GameObject root)
    {
        var rects=root.GetComponentsInChildren<RectTransform>(true);
        var screen=rects.Single(r=>r.name=="BackgroundCRT");
        var housing=rects.Single(r=>r.name=="Television");
        float bottom=screen.anchoredPosition.y-screen.sizeDelta.y*.5f;
        float top=Mathf.Min(screen.anchoredPosition.y+screen.sizeDelta.y*.5f,
            housing.anchoredPosition.y+housing.sizeDelta.y*.5f-50);
        if(top<=bottom) throw new InvalidOperationException("CRT glass does not fit within its housing.");
        screen.sizeDelta=new Vector2(screen.sizeDelta.x,top-bottom);
        screen.anchoredPosition=new Vector2(screen.anchoredPosition.x,(top+bottom)*.5f);
        var material=housing.GetComponent<UnityEngine.UI.Image>().material;
        material.SetVector("_HousingSize",new Vector4(housing.sizeDelta.x,housing.sizeDelta.y,screen.sizeDelta.x,screen.sizeDelta.y));
        Vector2 offset=screen.anchoredPosition-housing.anchoredPosition;
        material.SetVector("_FaceOffset",new Vector4(offset.x,offset.y,0,0));
        Save(material);
    }
    private static void ApplyGrounding(Scene scene)
    {
        var root=scene.GetRootGameObjects().Single(g=>g.GetComponent<StageSelectController>()!=null);
        RectTransform Find(string name)=>root.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name==name);
        var world=Find("Theatre");
        var room=Find("DarkBackdrop").GetComponent<UnityEngine.UI.Image>();
        var roomMaterial=Atmosphere("PixelRoom",6);
        roomMaterial.SetVector("_PixelGrid",new Vector4(320,180,0,0));
        roomMaterial.SetFloat("_Exposure",.32f); Save(roomMaterial);
        room.material=roomMaterial;

        var televisionShadow=Image("TelevisionContactShadow",world,new Vector2(0,-179),new Vector2(910,42),Atmosphere("GroundContactShadow",7));
        televisionShadow.color=new Color(1,1,1,.88f);
        var consoleShadow=Find("ConsoleContactShadow").GetComponent<UnityEngine.UI.Image>();
        consoleShadow.material=televisionShadow.material;
        Set(consoleShadow.rectTransform,new Vector2(0,-317),new Vector2(760,44));
        consoleShadow.color=new Color(1,1,1,.92f);
        Set(Find("CRTGlassReflection"),new Vector2(0,-252),new Vector2(920,150));
        Set(Find("ConsoleFloorReflection"),new Vector2(0,-355),new Vector2(660,80));
        Find("ConsoleFloorReflection").GetComponent<UnityEngine.UI.RawImage>().color=new Color(.9f,.85f,.96f,.75f);
        var cable=Image("ConsoleToTelevisionCable",world,new Vector2(420,-200),new Vector2(260,200),Atmosphere("AVCable",8));
        cable.color=Color.white;
        // Ground details must be visible around the hardware, while both cable ends disappear behind it.
        string[] groundOrder={"ScreenLightSpill","ReflectedCRTLight","CRTGlassReflection","ConsoleFloorReflection",
            "TelevisionContactShadow","ConsoleContactShadow","ConsoleToTelevisionCable"};
        for(int i=0;i<groundOrder.Length;i++) Find(groundOrder[i]).SetSiblingIndex(i+1);

        var music=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<GameBgmController>(true)).Single();
        Edit(music,so=>so.FindProperty("initialState").enumValueIndex=(int)GameBgmController.BgmState.None);
        var source=new SerializedObject(music).FindProperty("bgmSource").objectReferenceValue as AudioSource;
        if(source!=null) Edit(source,so=>so.FindProperty("m_PlayOnAwake").boolValue=false);
    }
    private static void ImportRoom()
    {
        string path=Art+"TheatreRoom.png";
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
        importer.filterMode=FilterMode.Point; importer.mipmapEnabled=false; importer.npotScale=TextureImporterNPOTScale.None;
        importer.textureCompression=TextureImporterCompression.Uncompressed; importer.maxTextureSize=2048; importer.SaveAndReimport();
    }
    private static Material CopyMaterial(Material source,string name)
    {
        string path=Art+name+".mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null) { material=new Material(source){name=name}; AssetDatabase.CreateAsset(material,path); }
        return material;
    }
    private static Material Atmosphere(string name,float mode)
    {
        string path=Art+name+".mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null) { material=new Material(Shader.Find("UI/IronBallGirl/StageSelectAtmosphere")){name=name}; AssetDatabase.CreateAsset(material,path); }
        material.SetFloat("_Mode",mode); Save(material); return material;
    }
    private static void Save(Material m) { EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m); }
    private static void Edit(UnityEngine.Object target,Action<SerializedObject> edit)
    { var so=new SerializedObject(target); edit(so); so.ApplyModifiedPropertiesWithoutUndo(); }
    private static void Ref(SerializedObject so,string name,UnityEngine.Object value) => so.FindProperty(name).objectReferenceValue=value;
    private static void Refs(SerializedObject so,string name,UnityEngine.Object[] values)
    { var array=so.FindProperty(name); array.arraySize=values.Length; for(int i=0;i<values.Length;i++) array.GetArrayElementAtIndex(i).objectReferenceValue=values[i]; }
    private static void Set(RectTransform rect,Vector2 position,Vector2 size) { rect.anchoredPosition=position; rect.sizeDelta=size; }
    private static RectTransform Rect(string name,Transform parent,Vector2 position,Vector2 size)
    {
        var rect=parent.Find(name) as RectTransform;
        if(rect==null) { rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false); }
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f); Set(rect,position,size); return rect;
    }
    private static UnityEngine.UI.Image Image(string name,Transform parent,Vector2 position,Vector2 size,Material material)
    {
        var rect=Rect(name,parent,position,size);
        var image=rect.GetComponent<UnityEngine.UI.Image>();
        if(image==null) image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.material=material; image.raycastTarget=false; return image;
    }
    private static UnityEngine.UI.RawImage Raw(string name,Transform parent,Vector2 position,Vector2 size,Material material)
    {
        var rect=Rect(name,parent,position,size);
        var image=rect.GetComponent<UnityEngine.UI.RawImage>();
        if(image==null) image=rect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        image.material=material; image.raycastTarget=false; return image;
    }
    private static TMP_Text Label(string name,Transform parent,Vector2 position,Vector2 size,TMP_Text template,float fontSize)
    {
        var rect=Rect(name,parent,position,size);
        var text=rect.GetComponent<TextMeshProUGUI>();
        if(text==null) text=rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font=template.font; text.fontSize=fontSize; text.color=template.color;
        text.alignment=TextAlignmentOptions.Center; text.raycastTarget=false; return text;
    }
}
