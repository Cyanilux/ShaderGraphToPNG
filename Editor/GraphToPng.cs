using System.Collections;
using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;

/*
-------------------------------------------------------------------------------------------------------------------------------------------------
	Shader Graph to PNG
-------------------------------------------------------------------------------------------------------------------------------------------------

Authors :
	GLURTH#7422
		- Reflection, Screenshot & Stitching and File regions
	Cyanilux (https://twitter.com/Cyanilux)
		- Processing region. Fixed position/scaling & stitching issues with the DoShaderGraphToPng function
		- Replaced EditorWindow with "Add Tool to Shader Graph Menu" region which adds "Graph To PNG" to the right-click menu in SG.
		- Cleaned up code in other regions

Setup:
	- Put this file in an Editor folder (or install as Package via git url)

Usage:
	1) Open up a Shader Graph. Close the Main Preview, Blackboard and Graph Inspector windows
	2) Zoom and move the graph, and size the window such that the graph you want to capture is visible
		- Tip : Can use the "A" keybinding to auto-focus the whole graph
	3) Right-click anywhere in the graph and select "Graph To PNG" from the dropdown menu
		- (If you don't see this listed in the menu, try restarting Shader Graph)
 		- Processing is done through a series of screenshots, so make sure nothing is covering the shader graph window
		- If you want to adjust the speed of capturing, see the timeBetweenScreenshots setting below
	4) When done, the image will be saved in Assets/ShaderGraphScreenshots, using the graph name, as printed in the Console window
 		- Note : Numbers may be appended to the filename to prevent overriding previous screenshots of the same graph
		- If you want to adjust which folder it goes in, see the savePath setting below

Known Issues :
	- Because the graph may be captured in multiple screenshots, previews that use the Time node may not be consistent

Tested with Unity 2020.3.0f1, Shader Graph v10.3.2
If there is any errors or problems with the final image, feel free to open an issue on the github and I'll try fixing it ~
https://github.com/Cyanilux/ShaderGraphToPNG

-------------------------------------------------------------------------------------------------------------------------------------------------
*/

[InitializeOnLoad]
public class GraphToPng {

	// Settings --------------------------------------------

	private static float timeBetweenScreenshots = 0.1f;
	private static string savePath = "/ShaderGraphScreenshots";

	//	----------------------------------------------------

	#region static Add Tool to Shader Graph Menu
	private static EditorWindow prev;
	private static GraphView prevGraphView;
	private static ContextualMenuManipulator manipulator;
	private static float initTime;

	static GraphToPng() {
		manipulator = new ContextualMenuManipulator(BuildContextualMenu);
		GetShaderGraphTypes();
		initTime = Time.realtimeSinceStartup;
		EditorApplication.update += CheckForGraphs;
	}

	static void BuildContextualMenu(ContextualMenuPopulateEvent evt) {
		evt.menu.AppendSeparator();
		evt.menu.AppendAction("Graph To PNG", OnMenuAction, DropdownMenuAction.AlwaysEnabled);
	}

	static void OnMenuAction(DropdownMenuAction action) {
		// Debug.Log("OnMenuAction");
		EditorWindow window = EditorWindow.focusedWindow;
		StartShaderGraphToPng(window.titleContent.text, window);
	}

	private static void CheckForGraphs() {
		if (Time.realtimeSinceStartup < initTime + 5f) return;
		// Delay as adding the manipulator wasn't always working properly on assembly reloads

		// This will make sure the current focused window always has the manipulator
		EditorWindow focusedWindow = EditorWindow.focusedWindow;
		if (focusedWindow !=  null && focusedWindow != prev) {
			if (focusedWindow.GetType().ToString().Contains("ShaderGraph")) {
				// is Shader Graph
				if (prevGraphView != null) {
					// If manipulator was in a previous graph, remove it
					prevGraphView.RemoveManipulator(manipulator);
				}

				// Add manipulator to graph
				GraphView graphView = GetGraphViewFromMaterialGraphEditWindow(focusedWindow);
				graphView.AddManipulator(manipulator);
				//Debug.Log("Added Manipulator (" + focusedWindow.titleContent.text + ")");

				prev = focusedWindow;
				prevGraphView = graphView;
			}
		}
	}
	#endregion

	#region static Reflection
	const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	private static Type materialGraphEditWindowType;
	private static Type userViewSettingsType;

	static void GetShaderGraphTypes() {
		Assembly assembly = Assembly.Load(new AssemblyName("Unity.ShaderGraph.Editor"));

		materialGraphEditWindowType = assembly.GetType("UnityEditor.ShaderGraph.Drawing.MaterialGraphEditWindow");
		userViewSettingsType = assembly.GetType("UnityEditor.ShaderGraph.Drawing.UserViewSettings");
	}

	// window:  MaterialGraphEditWindow  member: m_GraphEditorView ->
	// VisualElement:  GraphEditorView   member: m_GraphView  ->
	// GraphView(VisualElement):  MaterialGraphView   
	static GraphView GetGraphViewFromMaterialGraphEditWindow(EditorWindow win) {
		if (materialGraphEditWindowType == null || userViewSettingsType == null) {
			GetShaderGraphTypes();
			if (materialGraphEditWindowType == null) return null;
		}

		FieldInfo visualElementField = materialGraphEditWindowType.GetField("m_GraphEditorView", bindingFlags);
		VisualElement graphEditorView = (VisualElement)visualElementField.GetValue(win);
		if (graphEditorView == null) return null;
		Type graphEditorViewType = graphEditorView.GetType();

		// (Cyan) Hide Blackboard, Preview and Inspector windows
		FieldInfo userViewSettingsField = graphEditorViewType.GetField("m_UserViewSettings", bindingFlags);
		object userViewSettings = userViewSettingsField.GetValue(graphEditorView);
		if (userViewSettings != null && userViewSettingsType != null){
			userViewSettingsType.GetField("isBlackboardVisible", bindingFlags).SetValue(userViewSettings, false);
			userViewSettingsType.GetField("isPreviewVisible", bindingFlags).SetValue(userViewSettings, false);
			userViewSettingsType.GetField("isInspectorVisible", bindingFlags)?.SetValue(userViewSettings, false);

			graphEditorViewType.GetMethod("UpdateSubWindowsVisibility", bindingFlags).Invoke(graphEditorView, null);
		}

		// Get Graph View
		FieldInfo graphViewField = graphEditorViewType.GetField("m_GraphView", bindingFlags);
		GraphView graphView = (GraphView)graphViewField.GetValue(graphEditorView);
		return graphView;
	}
	#endregion

	#region static Screenshot & Stitching

	static Color[] ReadScreenPixel(Rect rectToRead) {
		return UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rectToRead.position, (int)rectToRead.width, (int)rectToRead.height);
	}

	static void StitchTiles(Color[] pixels, Vector2Int size, Color[] fullPixels, Vector2Int position, Vector2Int fullSize) {
		if (fullPixels.Length != fullSize.x * fullSize.y) Debug.Log("size mismatch-  add to array is not == to sizeOfFinal");
		if (pixels.Length != size.x * size.y) Debug.Log("size mismatch-  added array is not == to addedPixelsSize");
		for (int y = 0; y < size.y; y++) {
			for (int x = 0; x < size.x; x++) {
				Vector2Int coordInFinal = new Vector2Int(x + position.x, y + position.y);

				// Ignore pixels if they're outside the final image bounds
				if (coordInFinal.x < 0 || coordInFinal.y < 0 ||
					coordInFinal.x > fullSize.x || coordInFinal.y > fullSize.y)
					continue;

				int pixelIndex = (y * size.x) + x;
				int fullIndex = (coordInFinal.y * fullSize.x) + coordInFinal.x;

				if (fullIndex < fullPixels.Length && pixelIndex < pixels.Length)
					fullPixels[fullIndex] = pixels[pixelIndex];
			}
		}
	}
	#endregion

	#region static File
	/// <summary>
	/// Saves the pixels as a png
	/// </summary>
	public static string Save(int width, int height, Color[] pixels, string name) {
		Texture2D t = new Texture2D(width, height, TextureFormat.RGBA32, false);
		t.SetPixels(pixels, 0);
		t.Apply();

		byte[] bytes = t.EncodeToPNG();
		UnityEngine.Object.DestroyImmediate(t, true);

		System.IO.Directory.CreateDirectory(Application.dataPath + savePath);
		string path = GetUniquePathName(name);
		System.IO.File.WriteAllBytes(path, bytes);
		Debug.Log(string.Format("Saved graph at {0}", path));
		AssetDatabase.Refresh();
		return path;
	}

	static string GetUniquePathName(string name) {
		string path = string.Format("{0}{1}/{2}.png", Application.dataPath, savePath, name);
		int i = 0;
		while (System.IO.File.Exists(path)) {
			path = string.Format("{0}{1}/{2}{3:000}.png", Application.dataPath, savePath, name, i);
			i++;
		}
		return path;
	}
	#endregion

	#region Processing
	static string filename;
	static EditorWindow shaderGraphWindow;
	static bool isProcessing = false;

	/// <summary>
	/// Starts the GraphToPng process
	/// </summary>
	public static void StartShaderGraphToPng(string filename, EditorWindow shaderGraphWindow) {
		GraphToPng.shaderGraphWindow = shaderGraphWindow;
		GraphToPng.filename = filename;
		isProcessing = true;
		EditorApplication.update += EditorUpdate;
	}

	/// <summary> 
	/// Stops the GraphToPng process.
	/// This is will be done automatically when finished but could probably use this to interrupt it too
	/// </summary>
	public static void StopShaderGraphToPng() {
		EditorApplication.update -= EditorUpdate;
		isProcessing = false;
		doProccessingEnumerator = null;
	}

	private static IEnumerator doProccessingEnumerator = null;
	private static float lastTime = 0;

	private static void EditorUpdate() {
		if (doProccessingEnumerator == null)
			doProccessingEnumerator = DoShaderGraphToPng();

		float t = 0.01f;
		if (doProccessingEnumerator.Current is float)
			t = Mathf.Max(t, (float)doProccessingEnumerator.Current);

		if (lastTime + t < Time.realtimeSinceStartup) {
			lastTime = Time.realtimeSinceStartup;
			isProcessing = doProccessingEnumerator.MoveNext();
		}
		if (!isProcessing)
			StopShaderGraphToPng(); //stops this function from being called again, and closes window
	}

	/// <summary>
	/// Main Function, called by EditorUpdate function above.
	/// Zooms in, moves around the Shader Graph, taking screenshots for each tile.
	/// Then stitches them together, and saves it as a PNG.
	/// </summary>
	private static IEnumerator DoShaderGraphToPng() {
		GraphView graphView = GetGraphViewFromMaterialGraphEditWindow(shaderGraphWindow);
		Rect windowScreenRect = graphView.worldBound;
		windowScreenRect.position += shaderGraphWindow.position.position;

		Vector3 originalPos = graphView.viewTransform.position;
		Vector3 originalScale = graphView.viewTransform.scale;

		// Get Top Left and Bottom Right Positions
		Matrix4x4 matrix = graphView.viewTransform.matrix.inverse;
		Vector2 topLeftInGraphSpace = -matrix.MultiplyPoint(Vector2.zero); // equal to "(Vector2)graphView.viewTransform.position / graphView.viewTransform.scale"
		Vector2 bottomRightInGraphSpace = -matrix.MultiplyPoint(windowScreenRect.size);
		//Debug.Log("topLeft : " + topLeftInGraphSpace + ", bottomRight : " + bottomRightInGraphSpace);

		// Calculate Graph Size, Number of Tiles to capture, and their sizes
		Vector2Int graphTotalSizeInPixels = Vector2Int.FloorToInt(topLeftInGraphSpace - bottomRightInGraphSpace);
		Vector2Int pixelsPerTile = Vector2Int.FloorToInt(windowScreenRect.size);

		float x = graphTotalSizeInPixels.x / (float)pixelsPerTile.x;
		float y = graphTotalSizeInPixels.y / (float)pixelsPerTile.y;
		float xR = graphTotalSizeInPixels.x % (float)pixelsPerTile.x;
		float yR = graphTotalSizeInPixels.y % (float)pixelsPerTile.y;

		Vector2Int numberOfTiles = Vector2Int.CeilToInt(new Vector2(x, y));
		Vector2Int lastTileSize = Vector2Int.FloorToInt(new Vector2(xR, yR));
		if (lastTileSize.x == 0)
			lastTileSize.x = pixelsPerTile.x;
		if (lastTileSize.y == 0)
			lastTileSize.y = pixelsPerTile.y;

		//Debug.Log("graphTotalSizeInPixels : " + graphTotalSizeInPixels + ", numberOfTiles : " + numberOfTiles);
		//Debug.Log("pixelsPerTile : " + pixelsPerTile + ", lastTileSize : " + lastTileSize);

		Vector2 graphSpaceTileOffset = windowScreenRect.size; // this offset might need to change if viewTransform.scale isn't 1?
		graphView.viewTransform.scale = Vector3.one;

		// Repaint to prevent blurry text in the final image as we've changed the zoom level
		shaderGraphWindow.Repaint();
		yield return 0.25f;

		// Capture tiles and stitch them together
		Color[] fullPixels = new Color[graphTotalSizeInPixels.x * graphTotalSizeInPixels.y];
		Vector2Int coordInFull = new Vector2Int(0, 0);
		for (int xTile = 0; xTile < numberOfTiles.x; xTile++) {
			Vector2Int tileSize = pixelsPerTile;
			if (xTile == numberOfTiles.x - 1) {
				tileSize.x = lastTileSize.x;
			}

			for (int yTile = numberOfTiles.y - 1; yTile >= 0; yTile--) {
				graphView.viewTransform.position = topLeftInGraphSpace - new Vector2(xTile * graphSpaceTileOffset.x, yTile * graphSpaceTileOffset.y);

				if (yTile == numberOfTiles.y - 1) {
					tileSize.y = lastTileSize.y;
				} else {
					tileSize.y = pixelsPerTile.y;
				}
				windowScreenRect.size = tileSize;

				// Capture
				shaderGraphWindow.Repaint();
				yield return timeBetweenScreenshots;
				Color[] tilePixels = ReadScreenPixel(windowScreenRect);

				// Save each tile (for debugging)
				//SaveScreenShot(test.x, test.y, tileBuffer, filename.Replace("*", ""));
				//Debug.Log("coordInFinal : " + coordInFinal);
				StitchTiles(tilePixels, tileSize, fullPixels, coordInFull, graphTotalSizeInPixels);

				coordInFull.y += tileSize.y;
			}

			coordInFull.x += tileSize.x;
			coordInFull.y = 0;
		}

		Save(graphTotalSizeInPixels.x, graphTotalSizeInPixels.y, fullPixels, filename.Replace("*", ""));

		// Reset view back to original state
		graphView.viewTransform.position = originalPos;
		graphView.viewTransform.scale = originalScale;
		shaderGraphWindow.Repaint();
		StopShaderGraphToPng();
	}
	#endregion
}