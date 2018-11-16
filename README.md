# BurstRender
Toying with some software rendering algorithms using Unity's new Burst compiler and job system.

![](https://i.imgur.com/Nl7WbBI.jpeg)

Here's some renders of a WIP implementation of Peter Shirley's Raytracing in One Weekend book, which I started with:
![](https://i.imgur.com/rKvQ1Cz.jpeg)
![](https://i.imgur.com/CpmjLDv.jpeg)

Note: performance in the unity editor is quite slow due to NativeArray access safety checks. You should be able to turn those off, but as of U2018.2.6 they seem to be on no matter how you configure it. Make a build, and things run many orders of magnitude faster.
