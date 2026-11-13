import AppKit
let root="/Users/kayosac/hukaton/output/card-designs/new-seven-2026-11-14/block-01"
var results=[[String:Any]]()
for id in ["water_jet","rockfall","evil_eye"] {
 let p=root+"/"+id+".png";let im=NSBitmapImageRep(data:try! Data(contentsOf:URL(fileURLWithPath:p)))!;var zero=0;var part=0;var minx=im.pixelsWide;var miny=im.pixelsHigh;var maxx=0;var maxy=0
 for y in 0..<im.pixelsHigh {for x in 0..<im.pixelsWide {let a=im.colorAt(x:x,y:y)!.alphaComponent;if a==0 {zero+=1}else {if a<1 {part+=1};minx=min(x,minx);miny=min(y,miny);maxx=max(x,maxx);maxy=max(y,maxy)}}}
 results.append(["id":id,"width":im.pixelsWide,"height":im.pixelsHigh,"hasAlpha":im.hasAlpha,"samplesPerPixel":im.samplesPerPixel,"zeroAlphaPixels":zero,"partialAlphaPixels":part,"bbox":[minx,miny,maxx,maxy],"corners":[im.colorAt(x:0,y:0)!.alphaComponent,im.colorAt(x:im.pixelsWide-1,y:0)!.alphaComponent,im.colorAt(x:0,y:im.pixelsHigh-1)!.alphaComponent,im.colorAt(x:im.pixelsWide-1,y:im.pixelsHigh-1)!.alphaComponent]])
}
let data=try! JSONSerialization.data(withJSONObject:results,options:[.prettyPrinted,.sortedKeys]);try! data.write(to:URL(fileURLWithPath:root+"/png-checks.json"));print(String(data:data,encoding:.utf8)!)
for (j,color) in [NSColor(white:0.95,alpha:1),NSColor(white:0.12,alpha:1)].enumerated(){
 let b=NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:1200,pixelsHigh:640,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0)!
 NSGraphicsContext.saveGraphicsState();NSGraphicsContext.current=NSGraphicsContext(bitmapImageRep:b);color.setFill();NSRect(x:0,y:0,width:1200,height:640).fill()
 for (i,id) in ["water_jet","rockfall","evil_eye"].enumerated(){NSImage(contentsOfFile:root+"/"+id+".png")!.draw(in:NSRect(x:i*400+10,y:25,width:380,height:570));(["Струя", "Камнепад", "Сглаз"][i] as NSString).draw(at:NSPoint(x:i*400+10,y:5),withAttributes:[.foregroundColor:j==0 ? NSColor.black:NSColor.white,.font:NSFont.systemFont(ofSize:16)])}
 NSGraphicsContext.restoreGraphicsState();try! b.representation(using:.png,properties:[:])!.write(to:URL(fileURLWithPath:root+"/preview-\(j).png"))
}
