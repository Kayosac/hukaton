import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

let root = "/Users/kayosac/hukaton/"
let dir = root + "output/card-designs/lure-update-2026-11-12/"
let cs = CGColorSpace(name: CGColorSpace.sRGB)!
let flags = CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue
func load(_ path: String) -> CGImage {
    let source = CGImageSourceCreateWithURL(URL(fileURLWithPath:path) as CFURL,nil)!
    return CGImageSourceCreateImageAtIndex(source,0,nil)!
}
func pixels(_ image: CGImage) -> [UInt8] {
    var bytes = [UInt8](repeating:0,count:image.width*image.height*4)
    bytes.withUnsafeMutableBytes { ptr in
        let ctx = CGContext(data:ptr.baseAddress,width:image.width,height:image.height,bitsPerComponent:8,bytesPerRow:image.width*4,space:cs,bitmapInfo:flags)!
        ctx.setBlendMode(.copy)
        ctx.draw(image,in:CGRect(x:0,y:0,width:image.width,height:image.height))
    }
    return bytes
}
var reports = [[String:Any]]()
let preview = CGContext(data:nil,width:416,height:816,bitsPerComponent:8,bytesPerRow:416*4,space:cs,bitmapInfo:flags)!
let names = ["lure"]
for (index,name) in names.enumerated() {
    let path = dir + name + ".png"
    let sprite = load(path), data = pixels(sprite)
    let w = sprite.width, h = sprite.height
    var transparent = 0, opaque = 0, partial = 0, edgeVisible = 0, maxEdgeAlpha: UInt8 = 0, solid = 0
    var minX = w, minY = h, maxX = -1, maxY = -1
    for y in 0..<h { for x in 0..<w {
        let a = data[(y*w+x)*4+3]
        if a == 0 { transparent += 1 }
        else {
            if a == 255 { opaque += 1 } else { partial += 1 }
            if a >= 250 { solid += 1 }
            minX = min(minX,x); minY = min(minY,y); maxX = max(maxX,x); maxY = max(maxY,y)
            if x == 0 || x == w-1 || y == 0 || y == h-1 { edgeVisible += 1; maxEdgeAlpha = max(maxEdgeAlpha, a) }
        }
    } }
    let delivered = root + "hukaton/Assets/Decks/Art/" + name + ".png"
    let equal = data == pixels(load(delivered))
    for side in 0..<2 {
        let c: CGFloat = side == 0 ? 0.94 : 0.12
        let x = index*416 + side*208
        preview.setFillColor(CGColor(red:c,green:c,blue:c,alpha:1))
        preview.fill(CGRect(x:x,y:0,width:208,height:816))
        preview.interpolationQuality = .high
        preview.draw(sprite,in:CGRect(x:x+8,y:512,width:192,height:288))
        preview.draw(sprite,in:CGRect(x:x+40,y:272,width:128,height:192))
        preview.draw(sprite,in:CGRect(x:x+56,y:64,width:96,height:144))
    }
    reports.append([
        "name":name,"width":w,"height":h,"alpha_info":sprite.alphaInfo.rawValue,
        "transparent_pixels":transparent,"opaque_pixels":opaque,"partial_alpha_pixels":partial,
        "visible_pixels_on_canvas_edge":edgeVisible,"max_edge_alpha":Int(maxEdgeAlpha),"alpha_at_least_250_pixels":solid,"visible_bounds":[minX,minY,maxX+1,maxY+1],
        "transparent_background":transparent>0,"decoded_rgba_equal_to_delivered":equal
    ])
    print("\(name) \(w)x\(h), transparent=\(transparent), solid=\(solid), partial=\(partial), edgeMax=\(maxEdgeAlpha), deliveredEqual=\(equal)")
}
let target = CGImageDestinationCreateWithURL(URL(fileURLWithPath:dir+"inspection-preview.png") as CFURL,UTType.png.identifier as CFString,1,nil)!
CGImageDestinationAddImage(target,preview.makeImage()!,nil)
precondition(CGImageDestinationFinalize(target))
let report: [String:Any] = ["cards":reports,"preview_widths":[192,128,96],"preview_backgrounds":["light gray","dark gray"],"visual_review_status":"pending"]
try JSONSerialization.data(withJSONObject:report,options:[.prettyPrinted,.sortedKeys]).write(to:URL(fileURLWithPath:dir+"png-verification.json"))
