# PgdGeImageCovnerter

一个用来把主流的图片格式转换为SoftPal的Pgd/Ge的图片格式

## 使用方式

 1. 从Release下载最新的构建
 2. 解压下载下来的zip，里面应该只有一个exe文件
 3. 把你要转换的图片拖到exe上
 4. 等程序执行完，关闭程序，转换的图片会放在你拖入的图片的同一个目录

## Q&A

### 怎么卡在正在压缩了

只是因为压缩很慢，我不会写压缩算法，你会的话欢迎来pr🙂  
或者选择不要压缩功能，找到`Compressor`类，把`No compression`那坨代码反注释掉 (或者下载NoCompression版本)

### Pgd3或者其他的支持吗

暂时没支持

## 鸣谢

 - https://github.com/crskycode/GARbro  有一些代码是直接从这里复制来的，算法也是看这里的
 - https://github.com/SixLabors/ImageSharp  图片加载库
