package motion.profile.mapper;

import atlantafx.base.theme.PrimerDark;
import javafx.application.Application;
import javafx.fxml.FXMLLoader;
import javafx.scene.Scene;
import javafx.stage.Stage;

public class App extends Application {

    public static Stage primaryStage;

    @Override
    public void start(Stage stage) throws Exception {
        primaryStage = stage;
        FXMLLoader loader = new FXMLLoader(getClass().getResource("/layout.fxml"));
        Scene scene = new Scene(loader.load());
        stage.setTitle("Motion Profile Mapper");
        stage.setScene(scene);
        stage.show();

    }

    public static void main(String[] args) {
        Application.setUserAgentStylesheet(new PrimerDark().getUserAgentStylesheet());
        launch();
    }
}
