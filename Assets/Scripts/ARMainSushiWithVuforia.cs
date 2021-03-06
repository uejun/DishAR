﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vuforia;
using OpenCVForUnity;

public class ARMainSushiWithVuforia : MonoBehaviour {

	// テクスチャ変換のオンオフのためのフラグ
	public static bool willChange = true;

    // デバッグフラグ
    public bool isDebug;
		
	// 入力用
	Texture2D cameraTexture; // UnityEngineから取得する画像
	Mat cameraMat; // OpenCVの入力画像. cameraTextureから毎フレームはじめに変換して得る

	// 出力用
	public GameObject outputCamera1; // 変換後の画像を撮影するためのカメラ. インスペクタで設定してください
	OutputCamQuad camQuad1; // outputCamera1が撮影するQuad. 変換後の画像を投影する

	public GameObject outputCamera2;
	OutputCamQuad camQuad2; 

	public GameObject outputCamera3;
	OutputCamQuad camQuad3;

	public GameObject outputCamera4;
	OutputCamQuad camQuad4;  

	public string TargetObjectName = "PlateImageTarget";
	
	public int s_threshold = 90;
	public int v_low_threshold = 40;
	public int cb_low_threshold = 150;
	public int cr_low_threshold = 140;
    public int cr_refined_threshold = 125;
    public double alpha = 0.7;

    BinaryMatCreator binaryMatCreator;
    SushiTextureCreator _textureCreator;
	Plate plate;

	Mat floodFillMask;
	IScorer scorer; // 領域判定のスコアラー
    bool targetLockedIn = false;
    bool targetOnPlate = true;

    public double cr_threshold_upper;
    public double cr_threshold_lower;
    public double s_threshold_upper;
    public double s_threshold_lower;
    public double v_threshold_upper;
    public double v_threshold_lower;

    void Start () {
		// 入力画像用
		cameraMat = new Mat (Screen.height, Screen.width, CvType.CV_8UC3);
		cameraTexture = new Texture2D (cameraMat.cols (), cameraMat.rows (), TextureFormat.ARGB32, false);

		// 出力画像用
		camQuad1 = outputCamera1.GetComponent<OutputCamQuad> ();
		camQuad2 = outputCamera2.GetComponent<OutputCamQuad> ();
		camQuad3 = outputCamera3.GetComponent<OutputCamQuad> ();
		camQuad4 = outputCamera4.GetComponent<OutputCamQuad> ();

		// ターゲットの初期化
		var targetBehaviour = GameObject.Find (TargetObjectName).GetComponent<ImageTargetBehaviour> ();
		plate = new Plate(targetBehaviour, cameraMat.rows ());

		// テクスチャマネージャーの初期化
		_textureCreator = new SushiTextureCreator();

        // BinaryCreator初期化
        binaryMatCreator = new BinaryMatCreator();
        binaryMatCreator.setCrUpper(cr_threshold_upper);
        binaryMatCreator.setCrLower(cr_threshold_lower);
        binaryMatCreator.setSUpper(s_threshold_upper);
        binaryMatCreator.setSLower(s_threshold_lower);
        binaryMatCreator.setVUpper(v_threshold_upper);
        binaryMatCreator.setVLower(v_threshold_lower);

    }

	
	void Update() {
		
		// 左ボタンクリック
		if (Input.GetMouseButtonUp(0)) {
    		Vector3 mousePosition = Input.mousePosition;
    		Debug.Log("LeftClick:"+mousePosition );
			scorer.setLocation((int)mousePosition.x, (int)(cameraMat.height()-mousePosition.y));
		}

	}

	void OnPostRender () {
		
		// UnityのTexture2DからOpencvのMatに変換
		UnityEngine.Rect wholeRect = new UnityEngine.Rect (0, 0, cameraTexture.width, cameraTexture.height);
		cameraTexture.ReadPixels (wholeRect, 0, 0, true);
		Utils.texture2DToMat (cameraTexture, cameraMat);

		/* 初期化 */
		
		OpenCVForUnity.Rect searchRect = null; // 探索領域矩形
		double v_threshold = 0.0; // 輪郭探索するための二値画像を作るときの明るさの閾値

		// Vuforiaでの皿の検出可否によって、以下を切り替える
		// ・探索対象領域
		if (targetOnPlate) {
			if (plate.isNotFound ()) {
				// print ("plate is untracked.");
				searchRect = new OpenCVForUnity.Rect (0, 0, cameraMat.cols (), cameraMat.rows ());
			} else if (plate.isTracked ()) {
				// カップの上面の矩形を探索対象矩形とする. 
				searchRect = plate.getTopSurfaceRect (cameraMat);
			}
		} else {
			searchRect = new OpenCVForUnity.Rect (0, 0, cameraMat.cols (), cameraMat.rows ());
		}
		
		if (plate.isTracked ()) {
			// print ("plate is tracked.");
			plate.update ();
		}

		scorer = new MaguroScorer (searchRect);
		
		try 
        {
            var hSVChannels = ARUtil.getHSVChannels(cameraMat);
            var yCrCbChannels = ARUtil.getYCrCbChannels(cameraMat);
			
			var matForDeveloper = cameraMat.clone();

            // カメラ入力画像から, searchRectサイズの二値画像を生成
            Mat binaryROIMat = binaryMatCreator.createBinaryMat(cameraMat, searchRect);

            // 二値画像&探索領域矩形で輪郭探索
            var contours = ARUtil.findContours (binaryROIMat, searchRect.tl ());

			// 領域候補群を作成 -> 候補にスコアをつける -> 最大スコアの候補を取得
            var regionSet = new RegionCandidateSet (contours)
                .elliminateByArea(searchRect, 0.01, 0.9)
                .score (scorer)
                .sort();

            if (targetOnPlate)
            {
                regionSet = regionSet.elliminateByInclusion(searchRect);
            }

            if (regionSet.candidates.Count == 0)
            {
                print("first candidates is 0");
				goto show;
			}

            regionSet.drawRects(matForDeveloper);

            var regions = new List<Region>();
           
			foreach (var candidate in regionSet.candidates)
            {

                // 領域作成
                var region = new Region (candidate, cameraMat);
                var refinedRegion = region.createRefienedRegion(yCrCbChannels[1], cr_refined_threshold);
                if (refinedRegion != null)
                {
                    regions.Add(refinedRegion);
                }
            }

            var filteredRegions = Region.elliminateByInclusionRect(regions);

           
            // 食品領域に貼りつけるテクスチャ作成
            var texture = _textureCreator.create(cameraMat, filteredRegions);

            if (texture == null)
            {
                print("regions is empty");
                goto show;
            }

			Mat alphaMask = Mat.zeros(cameraMat.size(), CvType.CV_8U);
            foreach (var region in filteredRegions)
            {
                Core.add(region.mask, alphaMask, alphaMask);
			}

			if (willChange)
            {
                _textureCreator.alphaBlend(cameraMat, texture, alphaMask, alpha);
			}
		
			// foodRegion.drawRect(matForDeveloper);
			Imgproc.rectangle (matForDeveloper, searchRect.tl (), searchRect.br (), new Scalar (0, 0, 255), 3);

            //camQuad4.setMat(texture);
            //camQuad3.setMat(alphaMask);
            camQuad2.setMat(matForDeveloper);
		}
        catch(System.Exception e)
        {
			print(e);
			goto show;
		}

		show:
		camQuad1.setMat (cameraMat);
	
	}

}
