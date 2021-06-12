
# Shader Graph to PNG

![GraphToPNG](/img.png)

**Tested with Unity 2020.3.0f1, Shader Graph v10.3.2**

Screenshots a Shader Graph in multiple sections, stitches them together and saves the result as a PNG. 

If there is any errors (in any Unity/SG version) or problems with the final image, feel free to open an issue and I'll try fixing it ~

### Setup:
- Install via Package Manager → Add package via git URL : `https://github.com/Cyanilux/ShaderGraphToPNG.git`
- Alternatively, download and put the GraphToPng script in an Editor folder anywhere in Assets

### Usage:
1) Open up a Shader Graph. Close the Main Preview, Blackboard and Graph Inspector windows
2) Zoom and move the graph, and size the window such that the graph you want to capture is visible
	- Tip : Can use the "A" keybinding to auto-focus the whole graph
3) Right-click anywhere in the graph and select "Graph To PNG" from the dropdown menu
	- (If you don't see this listed in the menu, try restarting Shader Graph)
 	- Processing is done through a series of screenshots, so make sure nothing is covering the shader graph window
4) When done, the image will be saved in Assets/ShaderGraphScreenshots, using the graph name, as printed in the Console window
 	- Note : Numbers may be appended to the filename to prevent overriding previous screenshots of the same graph

### Known Issues :
- Because the graph may be captured in multiple screenshots, previews that use the Time node may not be consistent. Maximising the Shader Graph window will capture using less screenshots so may help reduce this.

### Authors :
- GLURTH#7422
	- Reflection, Screenshot & Stitching and File regions
- Cyanilux (https://twitter.com/Cyanilux)
	- Processing region. Fixed position/scaling & stitching issues with the DoShaderGraphToPng function
	- Replaced EditorWindow with "Add Tool to Shader Graph Menu" region which adds "Graph To PNG" to the right-click menu in SG.