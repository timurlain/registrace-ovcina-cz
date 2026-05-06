using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegistraceOvcina.Web.Migrations
{
    /// <inheritdoc />
    public partial class SeedFeedbackQuestions2026 : Migration
    {
        // Curated kid + adult feedback question schemas for the most recently
        // completed Ovčina game (canonical name "30. Ovčina Balinova pozvánka",
        // ended 2026-05-02). Placeholders only seeded if the column is NULL —
        // safe to re-run, and will not stomp organizer edits.
        //
        // JSON is embedded via PostgreSQL dollar-quoted strings ($json$...$json$)
        // so curly braces / quotes / Czech characters survive without escaping.

        private const string KidQuestionsJson = """
[{"key":"strongest_moment","label":"Jaký jsi měl nejsilnější zážitek ze hry, negativní/pozitivní?","helpText":"Klidně oba. Krátké poctivé věty stačí.","placeholders":["Získal jsem mitrilovou zbroj a cítil se silnější.","Druhý den jsme bojovali sedmkrát a ani jednou nevyhráli.","Porazili jsme osm příšer a dostali za to peníze i zkušenosti.","Stal jsem se rytířem a dostával výplatu z dobytých vesnic.","V Morii bylo dlouhé čekání na svůj boj.","Hned ráno jsem našel tři many a měl skvělý začátek.","Vytáhl jsem správnou runu z pytlíku, to byla nejlepší chvíle.","Skrýše byly moc těžké, skoro nic jsem nenašel.","Boj s drakem nás zničil, ale stálo to za to.","Někdo mi pořád bral many a černé kameny."]},{"key":"kingdom_role","label":"Co byla tvá role ve hře, měl jsi něco, čím jsi se bavil (pracoval jsem pro krále, zabíjel nestvůry...)?","placeholders":["Pomáhal jsem králi a obsazoval vesnice.","Chtěl jsem být co nejvíc nabušený a pak pomáhat ostatním.","Byla jsem alchymistka a míchala lektvary.","Bavilo mě prozkoumávat lokace a sbírat razítka.","Byl jsem zloděj, ale nejvíc mě bavilo bojování.","Stala jsem se královskou poselkyní, lítala mezi královstvími.","Lovil jsem artefakty a plnil questy od krále.","Hrála jsem mága, soustředila se na kouzla.","Sloužil jsem jako šlechtic a rozšiřoval své království.","Bavilo mě obchodování, sháněl jsem lepší zbraně."]},{"key":"changes_noticed","label":"Letošní Ovčinu bylo pár změn (kroužky pro kouzelníky, dungeon, konec hry) — čeho sis všiml a co si o tom myslíš?","placeholders":["Kroužky u kouzelníka jsou lepší, nemusí se pořád běhat pro manu.","Dungeon byl letos zdarma, šel jsem tam několikrát.","Závěr v Morii se mi líbil mnohem víc než loni.","Líbilo se mi, že jsem mohl hrát dál, i když jsem zemřel.","Nové kruhy u čaroděje vypadají dobře a fungují dobře.","Dřepy místo chleba mě potěšily, dalo se to zvládnout.","Kostka navíc pro lučištníka v prvním kole, super věc.","Magie mě zaujala, je toho víc než jen oheň.","Dungeon mohl být ale o něco těžší.","Mohli jsme se v Morii oživovat a hrát dál — paráda."]},{"key":"chronicle_story","label":"Máš nějaký příběh z letošní Ovčiny, který bychom mohli zapsat do kroniky?","placeholders":["Šli po mně dva salamandři, vzal jsem jim šupiny a koupil za ně super věci.","Brácha mi pomáhal s questem na prsten, schovali jsme se a utekli.","Ztratila jsem se v dungeonu, šla po fáborcích a našla cestu ven.","Strašně dlouho jsem hledal poklad, prohledal padesát lokací.","Stala jsem se alchymistkou a naučila se míchat lektvary.","Našel jsem starce u knihovny, postavili jsme tábor a v noci nás napadl salamandr.","Pomohli jsme si s kamarádkou jednu lokaci, kterou nikdo jiný nenašel.","Spadla mi spoluhráčka, vlekla jsem ji za sebou až do města.","Lojzík tahal povoz sám, a já se schovávala před příšerami.","Porazili jsme silného draka a dostali za něj poklad."]},{"key":"moria_opinion","label":"Co si myslíš o Morii (jestli se ti líbila nebo ne)?","placeholders":["Morie se mi moc líbila, závěr byl mnohem lepší než loni.","Bylo to těžké, ale díky tomu i napínavé.","Mohli jsme si vybrat obtížnost, to bylo super.","Užila jsem si Morii, naše příšera byla vtipná a hodná.","Morie byla těžká, dostali jsme se jen na druhý level.","Líbilo se mi, že jsme nemuseli čekat, když někdo umřel.","Bylo nás málo času, chtěla bych bojovat ještě víc.","Atmosféra v Morii byla úplně jiná, parádní.","Možnost dělat dřepy místo chleba se mi moc líbila.","Bitvu jsme prohráli, ale stálo to za to."]},{"key":"anything_else","label":"Prosím cokoli dalšího, co tě napadá:","placeholders":["Začátek hry byl moc těžký, slabší příšery na začátku by pomohly.","Více týmových questů by bylo super.","Mág by mohl mít víc typů kouzel.","Souboje mě stresují, nejvíc mě bavilo prozkoumávat lokace.","Zvířátka by mohla zůstat až do konce hry.","Akademie by mohla být jako další území.","Pro nováčky by se hodily lepší tipy, jak hrát.","Líbilo by se mi, kdybychom měli víc levelů.","Skrýše byly moc těžké.","Líbilo by se mi víc questů pro celou skupinu."]},{"key":"character_story","label":"Příběh tvé postavy:","placeholders":["Začal jsem jako trpaslík válečník, hledal jsem mitrilovou zbroj celé dva dny.","Stala jsem se alchymistkou jezerního lidu, mám velkou knihu lektvarů.","Sloužil jsem králi jako rytíř a dobyl tři vesnice.","Byl jsem zloděj, kradl ze skrýší a pomáhal vlastní rodině.","Hrála jsem mága z elfího lidu, naučila se silná kouzla.","Přišel jsem do Ovčiny jako hraničář, znal jsem každý kout lesa.","Začal jsem slabý, ale postupně se vypracoval na pátou úroveň.","Byla jsem královská poselkyně, nosila zprávy mezi královstvími.","Hrál jsem střelce z Nového Arnoru, nejlíp jsem si poradil s draky.","Byla jsem trpaslík zloděj, často jsem zachraňovala kamarády ve městech."]}]
""";

        private const string AdultQuestionsJson = """
[{"key":"strongest_moment","label":"Jaký byl tvůj nejsilnější zážitek z letošní Ovčiny, pozitivní/negativní?","helpText":"Klidně oba. Krátké poctivé věty stačí.","placeholders":[]},{"key":"kingdom_role","label":"Jakou jsi měl/a roli během akce a co tě nejvíc bavilo?","placeholders":[]},{"key":"changes_noticed","label":"Čeho sis všiml/a v letošních změnách (kroužky pro kouzelníky, dungeon, závěr v Morii) a co si o tom myslíš?","placeholders":[]},{"key":"chronicle_story","label":"Máš nějaký příběh nebo moment, který bychom mohli zapsat do kroniky?","placeholders":[]},{"key":"moria_opinion","label":"Co říkáš na letošní závěr v Morii?","placeholders":[]},{"key":"anything_else","label":"Cokoli dalšího, co tě napadá:","placeholders":[]},{"key":"character_story","label":"Tvůj příběh z Ovčiny:","placeholders":[]}]
""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Target the most recently completed "30. Ovčina ..." game whose
            // FeedbackKidQuestionsJson is still NULL. Idempotent: re-running
            // the migration after organizers have edited the JSON is a no-op.
            migrationBuilder.Sql($$"""
                UPDATE "Games"
                SET "FeedbackKidQuestionsJson" = $kid${{KidQuestionsJson}}$kid$
                WHERE "Id" = (
                    SELECT "Id" FROM "Games"
                    WHERE "Name" LIKE '30. Ovčina%'
                      AND "FeedbackKidQuestionsJson" IS NULL
                    ORDER BY "EndsAtUtc" DESC
                    LIMIT 1
                );

                UPDATE "Games"
                SET "FeedbackAdultQuestionsJson" = $adult${{AdultQuestionsJson}}$adult$
                WHERE "Id" = (
                    SELECT "Id" FROM "Games"
                    WHERE "Name" LIKE '30. Ovčina%'
                      AND "FeedbackAdultQuestionsJson" IS NULL
                    ORDER BY "EndsAtUtc" DESC
                    LIMIT 1
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse only on the same defensive prefix. Keeps any unrelated
            // game (e.g. a future "31. Ovčina") untouched.
            migrationBuilder.Sql("""
                UPDATE "Games"
                SET "FeedbackKidQuestionsJson" = NULL,
                    "FeedbackAdultQuestionsJson" = NULL
                WHERE "Name" LIKE '30. Ovčina%';
                """);
        }
    }
}
