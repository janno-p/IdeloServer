# Kasutajaliidesed (2014)

## Labor 2: AJAX kasutajaliidese viimistlemine HTML/CSS/JavaScript abil

*Labori kirjeldus on võetud aadressilt [http://lambda.ee/wiki/Serverirakenduse_praktiline_juhend](http://lambda.ee/wiki/Serverirakenduse_praktiline_juhend), mille materjalid on kasutatavad [GNU Free Documentation License 1.2](http://www.gnu.org/copyleft/fdl.html) tingimustel.*


### Lühikokkuvõte ja lahendusvariandid

Teises laboratoorses töös on vaja kasutajaliides reaalselt tööle panna. Selleks vaja:

* Failid tõsta serverisse.
* Panna andmete salvestamine ja otsimine kasutajaliidese külge.

Andmeid peab selles laboratoorses töös salvestama, otsima ja lugema
[AJAXi](http://en.wikipedia.org/wiki/Ajax_%28programming%29) ehk
[asünkroonse javascriptiga](http://en.wikipedia.org/wiki/XMLHttpRequest)
[JSON](http://en.wikipedia.org/wiki/JSON) formaadis. Teisisõnu, serveris olev programm ei ehita
valmis terviklikku html lehte, vaid ainult data-stringi, mille formattimise html-ks teeb javascript.
Datat kirjutab ja loeb samuti javascript.

Sa võid soovi korral ise valida serveri, kuhu oma failid panna, samuti võid ise valida
programmeerimiskeele, millega teha andmeid lugev ja välja andev rakendus, samuti võid ise valida
andmebaasisüsteemi ja schema.

Samas on sul võimalus kasutada olemasolevat Dijkstra serverit ja Dijkstras selle praktikumi jaoks
juba valmisehitatud rakendust ja andmebaasi: põhimõtteliselt võid täielikult vältida serveripoolse
andmerakenduse ise-ehitamist. Vahepealse alternatiivina võid võtta nimetatud valmisehitatud
rakenduse koodi ja schema ja teha temast Dijkstrasse oma variant, mida võid siis enda soovi kohaselt
muuta või edasi arendada.

Dijkstra kasutamisest ja valmisehitatud rakendusest on juttu siin lehel allpool.

Ajaxi kasutamise kaks väikest näitefaili harjutustunnist:

* minimaalne ajaxi näide mis töötab juhul, kui paned ta Dijkstra serverisse (muidu ei luba brauser
  Dijkstrast datat lugeda). [Sama fail töötavana Dijkstras](http://dijkstra.cs.ttu.ee/~tammet/ajax.html)

```html
<html>
<head>
<script>
var request;

function myupdatefun() {
  var response;
  alert("myupdatefun called with"+request.readyState)
  if (request.readyState == 4) {
       alert("Server is done, request.readyState == 4!");
       if (request.status == 200) {
          alert("Server sent data ok,request.status == 200!");
          response = request.responseText
          alert("response is: "+response)
          document.getElementById('sisu2').innerHTML=response;
       } else if (request.status == 404)
         alert("Request URL does not exist");
       else
         alert("Error: status code is " + request.status);
  }
}

function ajaxcall() {
  var url;
  request = new XMLHttpRequest();
  url = "http://dijkstra.cs.ttu.ee/~tammet/cgi-bin/otsi.py?salvestaja=tanel&table=t1"
  request.open("GET", url, true);
  request.onreadystatechange = myupdatefun;
  request.send(null);
}


</script>
<body>
Tere kah!
<form>
<input type="button" value="Proovi!" onclick="ajaxcall()">
</form>

<div id="sisu2"></div>
</body>
```

* veidi täiendatud ajaxi näide ja sama fail töötavana Dijkstras (kirjuta otsiväljale t0 ja vajuta
  nuppu)
* väike eval-i kasutamise näide jsoni formaadi parsimise selgituseks
