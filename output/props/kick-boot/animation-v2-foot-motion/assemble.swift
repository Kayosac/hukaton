import AppKit
import Foundation
import ImageIO
import UniformTypeIdentifiers
let root="/Users/kayosac/hukaton/output/props/kick-boot/animation-v2-foot-motion/"
struct Raster {
 let w:Int;let h:Int;var d:[UInt8]
 init(_ w:Int,_ h:Int){self.w=w;self.h=h;d=Array(repeating:0,count:w*h*4)}
 init(_ path:String){let r=NSBitmapImageRep(data:try! Data(contentsOf:URL(fileURLWithPath:path)))!;w=r.pixelsWide;h=r.pixelsHigh;d=Array(repeating:0,count:w*h*4);for y in 0..<h{for x in 0..<w{var p=[Int](repeating:0,count:4);r.getPixel(&p,atX:x,y:y);for k in 0..<4{d[(y*w+x)*4+k]=UInt8(p[k])}}}}
 func pixel(_ x:Int,_ y:Int)->[UInt8]{if x<0 || y<0 || x>=w || y>=h{return [0,0,0,0]};let i=(y*w+x)*4;return Array(d[i..<i+4])}
 mutating func put(_ x:Int,_ y:Int,_ p:[UInt8]){if x<0 || y<0 || x>=w || y>=h{return};let i=(y*w+x)*4;for k in 0..<4{d[i+k]=p[k]}}
 func rep()->NSBitmapImageRep{let r=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:w,pixelsHigh:h,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bitmapFormat:.alphaNonpremultiplied,bytesPerRow:w*4,bitsPerPixel:32)!;d.withUnsafeBytes{r.bitmapData!.update(from:$0.baseAddress!.assumingMemoryBound(to:UInt8.self),count:d.count)};return r}
 func save(_ name:String){try! rep().representation(using:.png,properties:[:])!.write(to:URL(fileURLWithPath:root+name))}
 func matte(_ scale:Int)->Raster{var r=Raster(w/scale,h/scale);for y in 0..<r.h{for x in 0..<r.w{let p=pixel(x*scale,y*scale),a=Double(p[3])/255;var q:[UInt8]=[0,0,0,255];for k in 0..<3{q[k]=UInt8((Double(p[k])*a+Double([48,50,57][k])*(1-a)).rounded())};r.put(x,y,q)}};return r}
}
let sourcePath="/Users/kayosac/hukaton/output/props/kick-boot/v1-psx/first-person-kick.png"
let src=Raster(sourcePath)
let names=["kick-01-intermediate.png","kick-02-intermediate.png","kick-03-full.png"]
var frames:[Raster]=[]
var checks:[[String:Any]]=[]
for i in 0..<3 {
 var r = i==2 ? src:Raster(root+"work/generated-0\(i+1).png")
 precondition(r.w==src.w && r.h==src.h)
 var transparent=0
 for y in 0..<r.h{for x in 0..<r.w{if r.pixel(x,y)[3]==0{transparent += 1}}}
 precondition(transparent>300000,"Generated background is not transparent")
 if i<2 {
  for y in 980..<src.h {for x in 0..<src.w {
   if y>=1100 {r.put(x,y,src.pixel(x,y));continue}
   let t=Double(y-980)/120.0,a=r.pixel(x,y),b=src.pixel(x,y);var p=a
   for k in 0..<4 {p[k]=UInt8((Double(a[k])*(1-t)+Double(b[k])*t).rounded())};r.put(x,y,p)
  }}
 }
 r.save(names[i]);let disk=Raster(root+names[i]);precondition(disk.d==r.d)
 var lowerMismatch=0
 for y in 1100..<src.h{for x in 0..<src.w{if disk.pixel(x,y) != src.pixel(x,y){lowerMismatch += 1}}}
 precondition(lowerMismatch==0)
 frames.append(disk);checks.append(["file":names[i],"lower_leg_rgba_mismatches_y1100_onward":lowerMismatch,"fully_transparent_pixels_before_compositing":transparent])
}
// Keep the original master file byte-for-byte for the fully extended frame.
try! Data(contentsOf:URL(fileURLWithPath:sourcePath)).write(to:URL(fileURLWithPath:root+names[2]))
var atlas=Raster(src.w*3,src.h)
for (i,f) in frames.enumerated(){for y in 0..<f.h{for x in 0..<f.w{atlas.put(i*src.w+x,y,f.pixel(x,y))}}}
atlas.save("kick-atlas.png")
let savedAtlas=Raster(root+"kick-atlas.png");var atlasMismatch=0
for (i,f) in frames.enumerated(){for y in 0..<f.h{for x in 0..<f.w{if savedAtlas.pixel(i*src.w+x,y) != f.pixel(x,y){atlasMismatch += 1}}}}
precondition(atlasMismatch==0)
var overview=Raster(1024,512)
for y in 0..<overview.h{for x in 0..<overview.w{overview.put(x,y,[48,50,57,255])}}
for (i,f) in frames.enumerated(){let p=f.matte(3);for y in 0..<p.h{for x in 0..<p.w{overview.put(i*341+x,y,p.pixel(x,y))}}}
overview.save("frames-preview.png")
let order=[0,0,1,2,2,1,0,0]
let gif=CGImageDestinationCreateWithURL(URL(fileURLWithPath:root+"kick-preview.gif") as CFURL,UTType.gif.identifier as CFString,order.count,nil)!
CGImageDestinationSetProperties(gif,[kCGImagePropertyGIFDictionary:[kCGImagePropertyGIFLoopCount:0]] as CFDictionary)
for i in order{CGImageDestinationAddImage(gif,frames[i].matte(2).rep().cgImage!,[kCGImagePropertyGIFDictionary:[kCGImagePropertyGIFDelayTime:0.10]] as CFDictionary)}
precondition(CGImageDestinationFinalize(gif))
let manifest:[String:Any]=["canvas":[src.w,src.h],"frames":checks,"atlas":"kick-atlas.png","atlas_size":[src.w*3,src.h],"columns":3,"rows":1,"cell_size":[src.w,src.h],"offset":[0,0],"padding":[0,0],"pivot":[0.5,0],"origin":"top-left","method":"Two imagegen ankle-articulation poses referenced to the original; lower leg below y=1100 copied exactly from original, transition y=980..1099. Full kick frame unchanged.","atlas_rgba_mismatches":atlasMismatch,"unity_import_validated":false]
try! JSONSerialization.data(withJSONObject:manifest,options:[.prettyPrinted,.sortedKeys]).write(to:URL(fileURLWithPath:root+"manifest.json"))
print("3 articulated frames; lower leg fixed; atlas exactly matches saved masters.")
